# LevelLibrary

A quality-of-life mod for A Dance of Fire and Ice. It started as "a level picker with search" and grew into a whole toolbox over time. Built for UnityModManager.
Includes integration with Iridium and a built-in YCH effect removal feature.

## What it does

**Level library**
A portal in the main menu that lists every custom level on your disk. Sort alphabetically, search by name, and open anything straight into the editor with one click. Levels are recorded automatically the first time you open them, and the mod keeps backups so a bad edit doesn't cost you your work.

**Effects removal**
Tired of a level's screen shake, camera moves and filters? There are 68 individual toggles grouped by category, plus a few built-in presets (remove decorations, remove camera events, remove filters) you can stack. You can set it globally in the settings page, or per level in the level details — a level with its own config ignores the global one.

**Backgrounds**
Play your own video, a static image, or a generated neural-style background behind levels. You can also replace the background music with any MP3/OGG and dim the background to your taste.

**Pet fish**
A little fish sits in the corner of the menus and says things. You can feed it lines, and you can build your own pet model with the separate Pet Model Editor: pick an image, adjust size and position, pick or draw a speech bubble, and export a `.llmd` pack that the mod imports. Packs support one or more opening lines that get picked at random.

**Portrait**
Put your own artwork on the lobby screen. Size, position and angle are all adjustable, and it parallax-scrolls along with the lobby.

**And the rest**
Local achievements, play-time stats, per-level records, favorites, notes, quick-level-pack management, and an in-mod update checker. If it saves you a trip to the file manager, it's probably in here.

## Installing

1. Install [UnityModManager](https://www.nexusmods.com/unitymodmanager) for A Dance of Fire and Ice.
2. Drop the `LevelLibrary` folder into `A Dance of Fire and Ice/Mods/`.
3. Launch the game. That's it.

## Feedback

Bugs and ideas are welcome on the mod page. If something breaks, the mod writes its own logs, so including your `Player.log` helps a lot.

## License

This mod is released under the MIT License. In short: use it, modify it, include it in your own projects — just keep the copyright notice, and don't blame us if something goes wrong.

The mod talks to your game installation (and to the mod's update server for version checks), but it never uploads your levels or personal data anywhere.

---

*made by luo, with help from cheng*

