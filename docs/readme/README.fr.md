# ConsGHost

[English](../../README.md) · [简体中文](README.zh-CN.md) · [Русский](README.ru.md) · [Español](README.es.md) · **Français**

ConsGHost (CGH) charge des mods en C# et C++ dans Garry's Mod sous Windows x64. Chaque mod peut s'exécuter dans le jeu ou dans un processus distinct. Un mod peut aussi contenir des extensions ordinaires de Garry's Mod sous forme de dossiers ou de fichiers `.gma`.

## Installation et lancement

1. Installez le [runtime .NET 10 Desktop pour Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) et connectez-vous à Steam. Il vous faut Windows 10 ou 11 et une installation de Garry's Mod contenant `gmod_win64.exe`.
2. Téléchargez `CGH-<version>-win-x64.zip` depuis la [page des versions sur GitHub](https://github.com/LytharaLab/ConsGHost/releases). Extrayez tout le contenu dans un dossier accessible en écriture en conservant l'arborescence de l'archive.
3. Lancez `ConsGHost.Host.exe`. Dans **Paramètres**, la dernière entrée de la barre latérale, choisissez le dossier contenant `gmod_win64.exe`. Steam peut ouvrir ce dossier via **Gérer → Parcourir les fichiers locaux**.
4. Vérifiez les mods activés dans le gestionnaire, puis lancez le jeu depuis CGH. L'archive contient les exemples BridgeDemo et Observer, qui illustrent les appels entre C# et C++ ainsi que les échanges de messages entre processus.

CGH lance Garry's Mod avec `-insecure` par défaut pour le développement local. Utilisez des sessions locales et chargez uniquement des mods et des DLL supplémentaires de confiance. Un processus distinct isole les plantages du mod, tout en conservant les droits de votre compte Windows.

Par défaut, CGH utilise la langue d’affichage de Windows et passe à l’anglais si cette langue est indisponible. Dans **Paramètres → Langue de l’interface**, choisissez l’anglais, le chinois simplifié, le russe, l’espagnol ou le français. Le choix prend effet immédiatement et reste enregistré au prochain lancement.

## Installation des mods

Placez chaque mod dans son propre dossier sous `Mods/`, à côté de `ConsGHost.Host.exe`. Le fichier `cghm_metadata.json` est obligatoire. Un mod peut également contenir `Managed/`, `Native/`, `Resources/`, `Addons/` et un modèle `config.default.json`. CGH crée `config.json` dès qu'il détecte un mod doté d'un modèle. Le [guide de développement de mods](../mod-development.md) décrit le SDK et le format des métadonnées.

CGH conserve les paramètres dans `cgh_settings.json`, les journaux dans `Sessions/` et les données des mods dans `Data/<mod-id>/`. Pour mettre à jour CGH, extrayez la nouvelle archive dans un autre dossier et transférez les mods et données à conserver. Gardez les fichiers de configuration des mods et vérifiez vos paramètres avant de relancer le jeu.

## Compilation depuis les sources

Installez le SDK .NET 10, Visual Studio 2022 Build Tools avec **Développement Desktop en C++**, le SDK Windows et CMake 3.24 ou ultérieur. Exécutez ces commandes dans PowerShell depuis la racine du dépôt.

```powershell
./build.ps1
./artifacts/CGH/ConsGHost.Host.exe
```

`./test.ps1` lance les tests natifs, d'intégration et de l'interface. `./package.ps1` génère le ZIP pour Windows x64 et son fichier SHA-256 dans `artifacts/`. Consultez le [guide de vérification](../verification.md).

## Documentation du projet

- [Architecture](../architecture.md)
- [Historique des modifications](../../CHANGELOG.md)
- [Licence et mentions relatives aux composants tiers](../../NOTICE.md)
- [Signalement des vulnérabilités et données de diagnostic](../../SECURITY.md)

ConsGHost est un projet communautaire indépendant. Son code et sa documentation d'origine sont distribués sous [licence MIT](../../LICENSE).
