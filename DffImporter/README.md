# BfBB .dff Importer for Unity

Drag-and-drop import of Battle for Bikini Bottom (RenderWare 3.x) `.dff` model files
into Unity, including geometry, materials/textures (via adjacent `.txd` files), and
skinned bone hierarchies.

## Installation

1. Copy the `Runtime` and `Editor` folders anywhere under your project's `Assets/`
   folder (e.g. `Assets/BfbbImport/Runtime` and `Assets/BfbbImport/Editor`).
   The `Editor` folder **must** be named `Editor` (or be inside an `Editor` folder)
   so Unity treats `DffImporter.cs` as editor-only code.
2. Let Unity recompile. Any `.dff` file already in your project will then show the
   "Import As" inspector for `DffImporter` — reimport it (right-click → Reimport)
   to trigger the importer.
3. Drop a `.dff` file (and, ideally, its matching `.txd`) into your `Assets` folder.
   Unity will import it automatically.

## Texture lookup

When importing `SpongeBob.dff`, the importer looks for `SpongeBob.txd` in the same
folder, and also picks up any other `.txd` files in that folder (BfBB levels often
share one texture dictionary across multiple models). Textures are matched to
materials by the texture name stored inside the `.dff`'s material chunks.

If you don't have `.txd` files extracted yet, you'll still get correctly shaped,
correctly skinned meshes — just untextured (flat-colored) materials.

## Xbox version specifics

This has been adjusted for the **Xbox** release of Battle for Bikini Bottom specifically:

- BfBB runs on **RenderWare 3.4.0.3**, and the Xbox build's `.dff` geometry chunks are the
  generic, platform-independent format (not pre-instanced/native like the PS2 version) — so
  `DffParser.cs`'s approach is correct for your files as-is.
- `.txd` textures on Xbox use RenderWare's Xbox native platform id (5), which shares the same
  header layout as D3D8/D3D9 PC rasters — `TxdParser.cs` now accepts ids 5, 8, and 9.
- The original Xbox GPU (NV2A) sometimes stores uncompressed raster data in a Z-order/Morton
  "swizzled" tile layout rather than row-major. `XboxUnswizzle.cs` detects the raster's swizzle
  flag and unswizzles before decoding pixels. DXT-compressed rasters aren't affected (DXT blocks
  aren't swizzled this way) and decode directly.
- PS2/GameCube-native rasters and pre-instanced geometry are still out of scope — not relevant
  to the Xbox build, but flagged in case you ever mix in files extracted from another platform's
  disc.

## What's supported

- Static and skinned (bone-weighted) geometry
- Multiple materials/submeshes per model
- Frame (bone/node) hierarchy → Unity Transform hierarchy
- Texture formats in `.txd`: 8-bit paletted, 16-bit (555/565/4444), 24/32-bit RGB(A),
  and DXT1/DXT3 compressed rasters

## Known limitations — read before reporting "broken" imports

This implementation targets the **generic, platform-independent RW3 chunk layout**
used by PC-side RenderWare tools and most BfBB PC-side modding pipelines. RenderWare
files extracted directly from console disc images can store geometry/textures in a
**native, platform-packed** layout (PS2 swizzled textures, console-specific vertex
buffers) that this parser intentionally does **not** try to decode — you'll get a
clear exception or a logged warning rather than a silently garbled mesh.

If your `.dff`/`.txd` files come from the PS2/GameCube/Xbox disc rather than a PC
build or a PC-targeted extraction tool, you may need to:
- Re-export/convert them with a tool that outputs generic RW3 format (several
  community DFF tools for this game do this), or
- Extend `TxdParser.ReadTextureNative` / `DffParser.ReadGeometry` to handle the
  specific native platform layout you're working with.

Other things not handled (out of scope for this pass):
- Vertex morph targets beyond the base (target 0) — parsed and skipped
- HAnim bone hierarchy metadata (parent/child bone IDs) beyond simple bone-id
  tagging — skin weights are bound by array order, which is correct for rendering
  but if you need an authored bone *hierarchy* for retargeting, you may want to
  extend `ParseFrameExtension` to read the full HAnim bone table
- Collision data, animation (.anm) files, and any RW plugin chunks not listed in
  `RwChunk`

## Batch import

Two menu commands are added for handling many models at once:

- **Right-click a folder in the Project window → "BfBB DFF/Reimport All .dff In Folder"**
  Force-reimports every `.dff` under that folder (recursively). Useful after updating the
  importer code/settings, since Unity doesn't always notice changes to importer logic on its own.

- **Menu bar → "Tools/BfBB DFF/Batch Import External Folder..."**
  Pick a folder *outside* the project (e.g. a folder of extracted game files), then a destination
  folder inside `Assets/`. It copies every `.dff`/`.txd` it finds (preserving subfolder structure)
  into the destination and triggers import on all of them in one pass.

## File overview

- `Runtime/RwCore.cs` — chunk header/binary reader plumbing
- `Runtime/RwModel.cs` — plain data classes (frames, geometry, materials, skin)
- `Runtime/DffParser.cs` — parses Clump → frames/geometry/materials/skin
- `Runtime/TxdParser.cs` — parses Texture Dictionary → Texture2D
- `Runtime/DxtDecoder.cs` — DXT1/DXT3 block decompression
- `Runtime/XboxUnswizzle.cs` — Xbox (NV2A) Z-order texture unswizzling
- `Runtime/ClumpBuilder.cs` — builds the Unity GameObject/Mesh/Material hierarchy
- `Editor/DffImporter.cs` — the `ScriptedImporter` that wires it all together
- `Editor/DffBatchImport.cs` — batch reimport / external-folder import menu commands

## If a specific file fails to import

Open the Console after importing — the importer logs a specific exception (bad
chunk type, unexpected end of section, etc.) rather than failing silently. That
message tells you which assumption about the chunk layout didn't hold for your
particular file, which is the fastest way to pin down whether you're dealing with
a console-native file or just a chunk-order quirk this version doesn't expect.
