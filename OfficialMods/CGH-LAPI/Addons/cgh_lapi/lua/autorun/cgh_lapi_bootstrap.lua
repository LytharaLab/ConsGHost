if MENU_DLL or not (SERVER or CLIENT) then return end
local realm = SERVER and "server" or "client"

local function loadBridge()
    -- The CGH native mod supplies this factory. In ordinary GMod it returns nil.
    local factory = CompileString("return nil", "CGH-LAPI/0.2.0/bootstrap", false)
    if type(factory) ~= "function" then return end
    local bridge = factory()
    if not bridge then return end

    function bridge.GetVersion()
        return {
            realm = realm,
            version = VERSION,
            versionString = VERSIONSTR,
            branch = BRANCH,
            lua = _VERSION,
            jit = jit and jit.version,
            arch = jit and jit.arch
        }
    end

    return bridge, assert(util.TableToJSON(bridge.GetVersion()), "could not encode realm version")
end

local bridge, version
local attached = false
local retryAt = 0
local attachError
local checkDeadline
local selfTestHook = "CGH-LAPI.SelfTest"

hook.Add("InitPostEntity", selfTestHook, function()
    hook.Remove("InitPostEntity", selfTestHook)
    if SERVER and not game.IsDedicated() then
        -- A local host command arrives after the host's client has entered the map.
        hook.Add("StartCommand", selfTestHook, function(player, cmd)
            if not player:IsListenServerHost() or cmd:IsForced() or cmd:CommandNumber() == 0 then return end
            hook.Remove("StartCommand", selfTestHook)
            checkDeadline = SysTime() + 10
        end)
    else
        checkDeadline = SysTime() + 10
    end
end)

local function checkBridge()
    if not attached then error("could not attach to CGH (error " .. tostring(attachError) .. ")", 0) end
    local reply, code = bridge._CheckVersion()
    if reply == nil then error("CGH version service returned error " .. tostring(code), 0) end
    if reply ~= version then error("CGH version service returned different realm metadata", 0) end
    return "GLua <-> CGH version round trip OK"
end

local function connect()
    if not bridge then
        bridge, version = loadBridge()
        if not bridge then return end
        cgh = bridge
    end
    local status
    status, attachError = bridge._Attach(version)
    attached = status == 0
end

connect()
hook.Add("Think", "CGH-LAPI.Dispatch", function()
    local now = SysTime()
    if not attached and now >= retryAt then
        retryAt = now + 0.25
        connect()
    end

    if checkDeadline and (attached or now >= checkDeadline) then
        checkDeadline = nil
        if not bridge then
            hook.Remove("Think", "CGH-LAPI.Dispatch")
            return
        end
        local ok, detail = pcall(checkBridge)
        Msg(string.format("[CGH-LAPI] Self-test %s [%s, %s]: %s\n",
            ok and "PASS" or "FAIL", realm, game.GetMap(), tostring(detail)))
    end

    if not attached then return end
    for i = 1, 32 do
        local kind, name, payload = bridge._Poll()
        if kind == nil then
            if name == -4 then attached = false end
            return
        end

        local ok, err
        if kind == 0 then
            ok, err = pcall(hook.Run, "CGHLAPIMessage", name, payload)
        else
            ok, err = pcall(hook.Run, name, payload)
        end
        if not ok then
            ErrorNoHaltWithStack("[CGH-LAPI] " .. tostring(err) .. "\n")
        end
    end
end)
