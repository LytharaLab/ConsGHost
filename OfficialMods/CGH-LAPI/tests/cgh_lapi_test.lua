if MENU_DLL or not (SERVER or CLIENT) then return end
local realm = SERVER and "server" or "client"
local mod = "cgh.lapi.test"
local payload = "bridge-你好\0bytes"
local seen = {}
local started = false
local finished = false

local function reportFailure(err)
    finished = true
    cgh.Call(mod, "failed", realm .. " " .. tostring(err))
end

local function mark(name, value)
    if not started or finished then return end
    if value ~= payload then
        reportFailure("bad payload for " .. name)
        return
    end
    seen[name] = true
    if seen.direct and seen.forced and seen.isolated and seen.isolatedHook then
        finished = true
        cgh.Call(mod, "passed", realm)
    end
end

hook.Add("CGHLAPIMessage", "CGH-LAPI.Test.Message", function(name, value)
    if name == "direct" or name == "isolated" then mark(name, value) end
end)
hook.Add("CGHLAPITestForce", "CGH-LAPI.Test.Force", function(value) mark("forced", value) end)
hook.Add("CGHLAPITestIsolated", "CGH-LAPI.Test.Isolated", function(value) mark("isolatedHook", value) end)

hook.Add("Think", "CGH-LAPI.Test.Start", function()
    if started or finished or not cgh or not cgh.GetVersion then return end
    local result, code = cgh.Call(mod, "echo", payload)
    if code == -4 then return end
    started = true
    local ok, err = pcall(function()
        local name = "CGH-LAPI/0.2.0/bootstrap"
        assert(CompileString("return nil", name)() == cgh, "Native bootstrap did not bind this realm")
        assert(CompileString("return 42", name)() == 42, "Bootstrap intercepted an unrelated chunk")
        assert(result == payload, "synchronous echo failed, code " .. tostring(code))
        local version = assert(cgh.Call(mod, "version", realm))
        local info = assert(util.JSONToTable(version))
        assert(info.realm == realm and info.version == VERSION and info.versionString == VERSIONSTR
            and info.branch == BRANCH and info.arch == "x64", "version mismatch " .. version)
        local missing, status = cgh.Call(mod, "does_not_exist", "")
        assert(missing == nil and status == -1, "missing service status")
        assert(cgh.Call(mod, "queue", realm) == "", "queue request")
        assert(cgh.Publish("cgh.lapi.test.request", realm) == 0, "publish request")
    end)
    if not ok then reportFailure(err) end
end)
