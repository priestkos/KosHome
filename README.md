<img src="images/icon.png" width="96">

# KosHome

One home for the PriestKos plugin tools. Nine former standalone Dalamud plugins live here as **apps** you switch on
and off from one window, laid out like a tablet: a home screen of app icons on frosted glass.

`/koshome` · Dalamud API 15 · .NET 10

> **No support will be provided. This software is to be used as is, without warranty of any kind.**

| App | Commands | What it does |
|---|---|---|
| Aetherpath | `/aetherpath`, `/ap` | Prices every way to get where you're going, and where your free and favored slots earn the most gil. |
| Calc14 | `/calc` | A fast, pretty calculator. |
| FrontlineAdvisor | `/fla` | Who is winning, who to hit, who is about to matter. |
| Gate Alert | `/gatealert`, `/gatetest` | Yells at you the moment your GATE opens in the Gold Saucer. |
| Nameplate Offset | `/npoffset` | Raise or lower the nameplate above a character's head. |
| Quill14 | `/quill` | Roleplay posts: smart splitting, markup, templates and paced sending. |
| VenueLedger | `/venue` | Every venue you know, on one clock. |
| Wardrobe14 | `/pmm` | A cross-collection view of your Penumbra mods. |
| WorldClock14 | `/tz`, `/tzconfig` | A widget on the home screen: your timezones live, one click to chat. |

All apps start switched on. `/koshome` opens the tablet:

- **Home:** a clock card (local time, Eorzea time, how many apps are running), the WorldClock14 widget (every zone
  live; click a zone to copy it for chat, or use its buttons to copy all, convert another time and set zones) and the app icons. Click an icon to open
  the app and again to close it; an app that is off is switched on first. A small light under an icon means its window
  is open; an orange dot means it isn't running, and hovering says why. Right-click an icon for Open/Close, Settings and
  Switch off.
- **Dock:** Home, Apps (every app with its switch, Open/Close and Settings buttons) and KosHome Settings.
- **KosHome Settings** (also the cog in `/xlplugins`): window opacity, frosted glass, the glow wallpaper and animations.
  Animations also stay off while Dalamud's Reduce motion is on.

Switching an app off removes its commands and windows until you switch it back on.

The look comes from [KosLib](https://github.com/priestkos/KosLib) (source-included as a git submodule):
`git clone --recurse-submodules`, or `git submodule update --init` in an existing clone.

## Moving from the standalone plugins

- **Uninstall the old plugin** in `/xlplugins`. KosHome won't start an app while its old plugin is loaded, and stops the
  app if the old plugin loads later; its icon in `/koshome` gets an orange dot that says why.
- **Settings carry over.** On first start each app reads the old plugin's settings file and from then on saves its own
  copy under `pluginConfigs/KosHome/`. The old file is never changed or deleted.

## Building

`dotnet build -c Release` (needs the Dalamud dev distribution). Tests: `pwsh tests/run-all.ps1`.
