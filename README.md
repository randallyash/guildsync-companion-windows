# GuildSync Companion for Windows

![GuildSync Companion](docs/home.png)

You play. We keep the character sheet up to date on [twilighttavern.co](https://twilighttavern.co).

The addon in-game writes a file called `GuildSync.lua`. This little Windows app uploads that file for you, so you can stop dragging it onto the website after every raid night.

Same idea as the [Linux tray app](https://forgejo.fifthdread.com/Fifthdread/guildsync-companion). This one is built for Windows.

## Grab it and go

Download [GuildSyncCompanion-Setup.exe](https://forgejo.fifthdread.com/Ramzal/guildsync-companion-windows/releases/download/v0.1.4/GuildSyncCompanion-Setup.exe) and double-click it.

1. Click **Install**, then **Finish**. No admin password. It installs just for your Windows account and drops a shortcut on the desktop and in the Start menu.
2. The app opens. Go to the [upload page](https://twilighttavern.co/upload), log in with Discord, and copy your upload token.
3. Paste that token in. If what you copied starts with `ffk_`, that's a Guild Hall key. Go back to the upload page and grab the token instead.
4. Let it find WoW: Forever. If it shrugs, browse to the folder that has `Wow.exe` and `_classic_beta_` in it.
5. It installs the addon. You're done.

Close the window whenever you want. It keeps running next to the clock and syncs when you log out. Left-click the icon if you need it again. The menu is Settings, Sync Now, Check for Updates, About, and Quit.

The big crest on the left is the tavern. Click it and the site opens.

Want it gone? Start menu, GuildSync Companion, Uninstall. Or Settings, Apps. Your token stays put, so reinstalling is painless.

## What it's doing while you play

- Watches `WTF\Account\*\SavedVariables\GuildSync.lua` and uploads it when it changes.
- Keeps an eye on things while the game is open. When you log out, one last sync, then it waits.
- Installs and updates the GuildSync addon from our Forgejo repo, and only inside `Interface\AddOns\GuildSync`.
- **Check for updates** is on Home, in Settings, and in the tray menu. Opening the app checks the addon and the app on its own. A newer addon is installed in place. A newer app asks first. Say yes and it closes, installs, and opens the window again. With automatic addon updates on, it looks for a new addon again about every 24 hours while it is running.
- **Characters** sits under Home. It is only the characters tied to your upload token, not the whole guild. You get the class-colored name, spec, race, level, item level, DKP, and when they were last seen. Click a name to open that character on the site. Pin the one you main and it stays at the top.

![Your characters](docs/characters.png)

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

This stays inside the rules Blizzard has published for addons and for third-party programs.

The in-game half is a normal addon. It uses the UI API, writes your character info to `GuildSync.lua` through SavedVariables, and costs nothing. Their addon policy allows that. Paid addons are the thing they ban.

The Windows app stays outside the client. It installs that addon in `Interface\AddOns\GuildSync` and uploads `GuildSync.lua` after the game has saved it. It leaves `Wow.exe`, the game archives, and the network protocol alone. It does not inject code, hook the process, read memory, send keystrokes, or play for you. When it checks whether you are logged in, it reads the process name, the same fact Task Manager shows.

Blizzard's August 2025 notice is about programs that modify the client: cheats and memory readers. An addon plus an uploader for the file that addon saved is the same kind of tool as Warcraft Logs. That is the lane this was built for.

The crest in the window is the Twilight Tavern guild mark from our site. We use our own art.

## License

MIT. See [LICENSE](LICENSE).
