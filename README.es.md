# SHierarchy Styler

[Read in English](README.md)

Herramienta pequeña para el editor de Unity que permite poner un icono y un color de fila a los GameObjects en la ventana Hierarchy. Pasas el ratón por una fila, pulsas el botón del lápiz que aparece a la derecha y eliges un icono de una lista.

La hice porque en escenas grandes me perdía entre tanto cubo gris. Los datos de icono y color se guardan por proyecto en `ProjectSettings/HierarchyStyler.json`, no en la escena, así que no toca tus archivos de escena.

## Rama para Unity 6.6

Esta rama es la versión para Unity 6.6 (6000.6). Unity 6.6 puso por defecto la nueva ventana Hierarchy hecha con UI Toolkit y convirtió en errores de compilación el callback antiguo de filas (`EditorApplication.hierarchyWindowItemOnGUI`) y los ids de objeto basados en int, así que el script 1.0 no compila ahí. Esta versión dibuja las filas con la nueva API `HierarchyWindow.BindViewItem` y sigue funcionando con la Hierarchy clásica (Edit > Project Settings > Editor > Hierarchy > Use Legacy Hierarchy). Los datos de versiones anteriores en `ProjectSettings/HierarchyStyler.asset` se migran al archivo JSON automáticamente la primera vez.

Nuevo en esta rama: clic derecho en una fila y elige "Hierarchy Style..." para abrir el mismo popup, o "Remove Hierarchy Style". Unity 6.6 también tiene una preferencia GameObject Icons (Edit > Preferences > General) que muestra el icono puesto en el Inspector, pero no tiene colores de fila, botón rápido, pack de iconos ni selección múltiple, así que la herramienta sigue siendo útil. Para Unity 6000.0 a 6000.5 usa la rama `main`.

## Qué hace

- Botón de lápiz en cada fila de la Hierarchy (solo se ve al pasar el ratón). Funciona con un objeto o con la selección actual.
- Popup con buscador y todos los iconos agrupados por carpeta, mostrando la imagen real para que veas lo que eliges.
- Color de fila opcional a partir de unos cuantos colores predefinidos.
- 218 iconos incluidos, repartidos en 21 carpetas: Organization, Environment, Lighting, Camera, Characters, Gameplay, Systems, Audio, Physics, VFX, UI, Animation, Debug, Horror, Weapons, Ammo, Loot, Vehicles, RPG, Multiplayer y SciFi. Cada carpeta usa su propio color para distinguirlas rápido.
- Deshacer y rehacer funcionan.
- Los objetos se identifican por su id guardado, así que renombrarlos o cambiarlos de padre no pierde el estilo.
- Puedes añadir tus propios PNG a la carpeta de iconos y aparecen en el popup.

## Instalación

Package Manager > `+` > Add package from git URL:

```
https://github.com/fantatik3/SHierarchyStyler.git#unity-6.6
```

O simplemente copia `Editor/HierarchyStyler.cs`, `Editor/HierarchyStyleStore.cs` y la carpeta `Editor/Icons` en algún sitio dentro de `Assets/`. Tienen que estar juntos, el script busca una carpeta llamada `Icons` (o `SHierarchyIcons`) en su mismo directorio.

Unity 6000.6 o más reciente. Para 6000.0 a 6000.5 instala desde la rama `main` (la URL sin `#unity-6.6`).

## Cómo usar

[![demo](https://i.imgur.com/lpScV36.gif)](https://imgur.com/lpScV36)

1. Guarda la escena primero (Ctrl+S). Los objetos de una escena que nunca se ha guardado no tienen id todavía, así que no se pueden estilizar hasta que lo hagas.
2. Pasa el ratón por una fila de la Hierarchy. Aparece un botón de lápiz a la derecha.
3. Púlsalo. El popup muestra el nombre del objeto, una fila de colores, un buscador y la lista de iconos.
4. Pulsa un color para ponerlo a la fila. El popup sigue abierto. El primer cuadro (con la línea diagonal) significa sin color.
5. Pulsa un icono para ponerlo. El popup se cierra.
6. Para estilizar varios objetos a la vez, selecciónalos y pulsa el lápiz en cualquiera de ellos. El título del popup dice cuántos objetos va a cambiar.
7. Para cambiar un estilo, pulsa el lápiz otra vez. El icono y el color actuales aparecen resaltados.
8. Para quitar un estilo, pulsa el lápiz y elige "Remove style" al principio de la lista. "No icon (color only)" mantiene el color y quita el icono.

Ctrl+Z / Ctrl+Y deshacen y rehacen todo esto.

## Añadir tus propios iconos

Deja archivos PNG en `Editor/Icons/<Carpeta>/Nombre.png`. El nombre de la carpeta es el nombre del grupo en el popup. Ajustes de importación que van bien: sin mipmaps, sin compresión, bilineal, alpha como transparencia. El tamaño da igual, la Hierarchy los dibuja a 16px.

## Limitaciones

- Solo objetos de escena. Prefab Mode y assets del proyecto se ignoran.
- El popup es propio porque el GenericMenu de Unity no puede mostrar imágenes en Windows.

## Licencia

Gratis para usar, copiar, modificar y compartir, también mientras trabajas en proyectos comerciales. No se puede vender ni meter en nada que cueste dinero (Asset Store incluida). Las copias tienen que seguir siendo gratis y conservar la licencia. Texto completo en [LICENSE](LICENSE).
