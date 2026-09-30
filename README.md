# GuildSync Companion for Windows

![GuildSync Companion](docs/home.png)

Keeps your World of Warcraft: Forever character data synced to [twilighttavern.co](https://twilighttavern.co). The in-game addon writes `GuildSync.lua`. This app uploads that file. You do not drag it onto the website after every session.

This is the Windows companion to the [Linux tray app](https://forgejo.fifthdread.com/Fifthdread/guildsync-companion). Same job, different machinery: Windows install locations, the process list, `%AppData%`, a Run-key startup entry, and a named pipe so a second launch just opens the window.

## What it does

- Watches `WTF\Account\*\SavedVariables\GuildSync.lua` and uploads it when it changes.
- While the game is open it keeps watching. When the game closes it does one last sync, then waits.
- Installs and updates the GuildSync addon from the public Forgejo repo, and only inside `Interface\AddOns\GuildSync`.
- Sits in the notification area. Left-click opens the window. The menu has Settings, Sync Now, Check for Addon Updates, About, and Quit.

## First run

1. Start **GuildSync Companion**.
2. Paste your upload token from the [Sync Gamedata page](https://twilighttavern.co/upload). Log in with Discord there. A Guild Hall key (`ffk_…`) is not a token.
3. Let it find the WoW: Forever install, or browse to the folder that contains `Wow.exe` and `_classic_beta_`.
4. It installs the addon and keeps running in the tray. It also offers to start with Windows.

## For guild members

Send them `GuildSyncCompanion-Setup.exe`. They double-click it. It installs for their Windows account, puts a shortcut on the desktop and in the Start menu, and opens the app. No administrator password.

1. If Windows says it protected the PC, choose **More info**, then **Run anyway**. The setup is unsigned until the guild has a code-signing certificate, so SmartScreen stops on that screen.
2. Click **Install**, then **Finish**.
3. Paste the upload token from [twilighttavern.co/upload](https://twilighttavern.co/upload). Log in with Discord on that page. The app finds the game and installs the addon.
4. Closing the window leaves it running beside the clock.

Uninstall is in the Start menu folder, or in Settings under Apps. The upload token is kept.

## Build

```bash
dotnet test
./scripts/build-windows.sh
```

That writes `dist/GuildSyncCompanion-Setup.exe`. The script also leaves the unpacked app in `dist/win-x64`. .NET does not need to be installed on the member's PC. Building the setup needs NSIS (`makensis`).

```bash
dotnet publish src/GuildSync.Companion -c Release -r win-x64 --self-contained true -o dist/win-x64
```

## Blizzard's rules

GuildSync Companion is not affiliated with, endorsed by, or signed by Blizzard Entertainment. Nothing in this repo is a Blizzard approval.

The desktop app stays outside the game client:

- It does not modify `Wow.exe`, game archives, or the client's network protocol.
- It does not inject a DLL, hook the process, read game memory, or send keystrokes.
- It does not bot, automate combat, or change gameplay.
- The only process check is the image name, the same fact Task Manager shows, so the app knows when you are playing and when you logged out.
- The only files it writes are the GuildSync addon folder, which is the normal `Interface\AddOns` path.
- The only file it reads for upload is `GuildSync.lua`, which the addon itself wrote through SavedVariables.
- The app and the addon are free. There is no paid unlock.

Blizzard's August 2025 notice targets third-party software that **modifies the game client**. Addons that use the UI API, and uploaders that read a file the addon saved, are a different category. Blizzard still has the last word on its own games. This README is not legal advice, and it is not a claim that Blizzard has reviewed this program.

The window uses the Twilight Tavern crest from the guild site. It does not ship Blizzard's logos or artwork.

## Signing

There is no Authenticode certificate in this repository, and a self-signed stand-in is not used. Windows SmartScreen only treats a file as a known publisher after it is signed with an OV or EV code signing certificate that a public CA issued to the publisher's legal name.

When the guild has that certificate, on a Windows machine with the Windows SDK:

```powershell
$env:SIGN_PFX_PASSWORD = '...'
.\scripts\sign-windows.ps1 -Pfx C:\path\guild.pfx -Exe .\dist\GuildSyncCompanion-Setup.exe
```

Sign the setup file. That is the one members download. SmartScreen looks at it before the installed app.

The script refuses a self-signed certificate. The `signtool` that ships with some Linux packages signs Java jars and cannot sign this exe.

## Theme

Colors match [twilighttavern.co](https://twilighttavern.co): near-black surfaces and the tavern gold. The crest is the guild mark from that site.

## License

MIT. See [LICENSE](LICENSE).
