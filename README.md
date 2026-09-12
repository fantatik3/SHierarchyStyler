# SHierarchy Styler

[Leer en español](README.es.md)

Small Unity editor tool for giving GameObjects an icon and a row color in the Hierarchy window. You hover a row, click the pencil button that shows up on the right and pick an icon from a list.

I made it because I kept losing track of what was what in big scenes and the default cube icon doesn't help. The color/icon data is stored per project in `ProjectSettings/HierarchyStyler.json`, not in the scene, so it doesn't touch your scene files.

## Note if you're on Unity 6.6 or newer

Unity 6.6 added this natively. Go to Edit > Preferences > General > Hierarchy Window and turn on GameObject Icons, and the hierarchy shows the icon you set on each GameObject (the one you pick by clicking the object's icon in the Inspector). So on 6.6 you wouldn't need this tool for the icons. It still gives you the row colors, the quick pencil button, the icon set and styling several objects at once, which the built-in option doesn't do. See the [Hierarchy window reference](https://docs.unity3d.com/6000.6/Documentation/Manual/hierarchy-reference.html) in the Unity manual.

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
https://github.com/fantatik3/SHierarchyStyler.git
```

Or just copy `Editor/HierarchyStyler.cs`, `Editor/HierarchyStyleStore.cs` and the `Editor/Icons` folder somewhere inside `Assets/`. They need to stay next to each other, the script looks for a folder called `Icons` (or `SHierarchyIcons`) in its own directory.

Unity 6000.0 or newer.

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
