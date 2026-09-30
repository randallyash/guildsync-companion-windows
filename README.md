# GuildSync Companion for Windows

![GuildSync Companion](docs/home.png)

You play. We keep the character sheet up to date on [twilighttavern.co](https://twilighttavern.co).

The addon in-game writes a file called `GuildSync.lua`. This little Windows app uploads that file for you, so you can stop dragging it onto the website after every raid night.

Same idea as the [Linux tray app](https://forgejo.fifthdread.com/Fifthdread/guildsync-companion). This one is built for Windows.

## Grab it and go

Download [GuildSyncCompanion-Setup.exe](https://forgejo.fifthdread.com/Ramzal/guildsync-companion-windows/releases/download/v0.1.0/GuildSyncCompanion-Setup.exe) and double-click it.

Windows is going to be dramatic about it. The window says Windows protected your PC. Click **More info**, then **Run anyway**. We don't have a paid code-signing certificate yet, so Windows treats us like a stranger. The app is ours. The steps below are the whole install.

1. Click **Install**, then **Finish**. No admin password. It installs just for your Windows account and drops a shortcut on the desktop and in the Start menu.
2. The app opens. Go to the [upload page](https://twilighttavern.co/upload), log in with Discord, and copy your upload token.
3. Paste that token in. If what you copied starts with `ffk_`, that's a Guild Hall key. Go back to the upload page and grab the token instead.
4. Let it find WoW: Forever. If it shrugs, browse to the folder that has `Wow.exe` and `_classic_beta_` in it.
5. It installs the addon. You're done.

Close the window whenever you want. It keeps running next to the clock and syncs when you log out. Left-click the icon if you need it again. The menu is Settings, Sync Now, Check for Addon Updates, About, and Quit.

Want it gone? Start menu, GuildSync Companion, Uninstall. Or Settings, Apps. Your token stays put, so reinstalling is painless.

## What it's doing while you play

- Watches `WTF\Account\*\SavedVariables\GuildSync.lua` and uploads it when it changes.
- Keeps an eye on things while the game is open. When you log out, one last sync, then it waits.
- Installs and updates the GuildSync addon from our Forgejo repo, and only inside `Interface\AddOns\GuildSync`.

## Building it

```bash
dotnet test
./scripts/build-windows.sh
```

That writes `dist/GuildSyncCompanion-Setup.exe`. Members don't need .NET installed. Building the setup needs NSIS (`makensis`).

If you only want the unpacked app:

```bash
dotnet publish src/GuildSync.Companion -c Release -r win-x64 --self-contained true -o dist/win-x64
```

## Blizzard

We're not Blizzard, and they haven't reviewed or endorsed this.

The app installs the GuildSync addon the normal way and uploads the file that addon saves. It leaves `Wow.exe` alone. It doesn't read game memory, inject anything, or play the game. To know when you're logged in, it looks at the process name, the same thing Task Manager shows. The app and the addon are free.

Blizzard's August 2025 note is about programs that modify the client. Addons and file uploaders are a different bucket, and they still get the last word on their game. This is us being straight with you, not legal advice.

The crest in the window is ours, from the guild site. No Blizzard logos.

## Signing

Nothing in here is signed. A self-signed certificate would still scare Windows, so we don't ship one.

When we have a real certificate, on a Windows machine with the Windows SDK:

```powershell
$env:SIGN_PFX_PASSWORD = '...'
.\scripts\sign-windows.ps1 -Pfx C:\path\guild.pfx -Exe .\dist\GuildSyncCompanion-Setup.exe
```

Sign the setup. That's the file people download. The script refuses a self-signed cert. The `signtool` that comes with some Linux packages signs Java jars and will not sign this exe.

## License

MIT. See [LICENSE](LICENSE).
