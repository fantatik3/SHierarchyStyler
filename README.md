# SHierarchy Styler

[Leer en español](README.es.md)

Small Unity editor tool for giving GameObjects an icon and a row color in the Hierarchy window. You hover a row, click the pencil button that shows up on the right and pick an icon from a list.

I made it because I kept losing track of what was what in big scenes and the default cube icon doesn't help. The color/icon data is stored per project in `ProjectSettings/HierarchyStyler.json`, not in the scene, so it doesn't touch your scene files.

## Unity 6.6 branch

This branch is the Unity 6.6 (6000.6) version. Unity 6.6 made the new UI Toolkit Hierarchy window the default and turned the old row callback (`EditorApplication.hierarchyWindowItemOnGUI`) and the int-based object ids into compile errors, so the 1.0 script does not build there. This version draws its rows through the new `HierarchyWindow.BindViewItem` API and still works with the legacy Hierarchy (Edit > Project Settings > Editor > Hierarchy > Use Legacy Hierarchy). Data from earlier versions in `ProjectSettings/HierarchyStyler.asset` is migrated to the JSON file automatically on first load.

New in this branch: right-click a row and pick "Hierarchy Style..." to open the same popup, or "Remove Hierarchy Style". Unity 6.6 also has a built-in GameObject Icons preference (Edit > Preferences > General) that shows the icon set in the Inspector, but it has no row colors, quick button, icon set or multi-select, so the tool is still useful. For Unity 6000.0 to 6000.5 use the `main` branch.

## What it does

- Pencil button on each hierarchy row (only visible while hovering). Works on one object or on the current selection.
- Popup with a search box and all the icons grouped by folder, with the actual images so you can see what you're picking.
- Optional row color from a small set of presets.
- 218 icons included, split in 21 folders: Organization, Environment, Lighting, Camera, Characters, Gameplay, Systems, Audio, Physics, VFX, UI, Animation, Debug, Horror, Weapons, Ammo, Loot, Vehicles, RPG, Multiplayer and SciFi. Each folder uses its own color so you can tell them apart quickly.
- Undo/redo works.
- Objects are identified by their saved id, so renaming or reparenting them keeps the style.
- You can add your own PNGs to the icons folder and they show up in the popup.

## Install

Package Manager > `+` > Add package from git URL:

```
https://github.com/fantatik3/SHierarchyStyler.git#unity-6.6
```

Or just copy `Editor/HierarchyStyler.cs`, `Editor/HierarchyStyleStore.cs` and the `Editor/Icons` folder somewhere inside `Assets/`. They need to stay next to each other, the script looks for a folder called `Icons` (or `SHierarchyIcons`) in its own directory.

Unity 6000.6 or newer. For 6000.0 to 6000.5 install from the `main` branch instead (URL without `#unity-6.6`).

## How to use

[![demo](https://i.imgur.com/lpScV36.gif)](https://imgur.com/lpScV36)

1. Save the scene first (Ctrl+S). Objects in a scene that was never saved don't have an id yet, so they can't be styled until you do.
2. Hover a row in the Hierarchy. A pencil button appears on the right side.
3. Click it. The popup shows the object name, a row of color swatches, a search box and the icon list.
4. Click a color swatch to set the row color. The popup stays open. The first swatch (with the diagonal line) means no color.
5. Click an icon to set it. The popup closes.
6. To style several objects at once, select them and click the pencil on any of them. The popup title tells you how many objects it will change.
7. To change a style, click the pencil again. The current icon and color are highlighted.
8. To remove a style, click the pencil and pick "Remove style" at the top of the list. "No icon (color only)" keeps the color and removes the icon.

Ctrl+Z / Ctrl+Y undo and redo any of this.

## Adding your own icons

Drop PNG files in `Editor/Icons/<Folder>/Name.png`. The folder name becomes the group name in the popup. Import settings that work well: no mipmaps, uncompressed, bilinear, alpha is transparency. Size doesn't matter much, the hierarchy draws them at 16px.

## Known limits

- Only scene objects. Prefab Mode and project assets are ignored.
- The popup is custom because Unity's GenericMenu can't show images on Windows.

## License

Free to use, copy, change and share, including while working on commercial projects. You can't sell it or put it in anything that costs money (Asset Store included). Copies have to stay free and keep the license. Full text in [LICENSE](LICENSE).
