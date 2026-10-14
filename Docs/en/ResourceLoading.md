# Loading assets and directory layout

[简体中文](../ResourceLoading.md) | [English](ResourceLoading.md)

## Asset ownership and logical paths

Assets belong to the content mod that uses them. Example layout:

```text
MyContentMod/
├─ About/About.xml
├─ LoadFolders.xml
└─ 1.7/
   ├─ Assemblies/MyContentMod.dll
   └─ Data/RimUnity/
      ├─ catalog.json
      └─ exports/<generation>/...
```

`LoadFolders.xml` can use `<v1.7><li>1.7</li></v1.7>` to load this version's assemblies and Defs. RimUnity's ModContentPack entry point reads `mod.RootDir/1.7/Data/RimUnity`; it does not automatically resolve alternative resource directories.

The catalog records the format version, owner, stable asset IDs, names, descriptions, logical paths, relative file paths, types and dependencies. Load an asset by its logical path, such as `Effects/BasicBurst`, rather than a path such as `exports/<generation>/xxx.json`, which can change between exports. The loader does not look for similarly named resources in other mods to fill missing dependencies.

## Public entry points

| API | Result or purpose |
|---|---|
| `PrefabLoader.Load(mod, path)` | Shared GameObject template from the specified content mod |
| `PrefabLoader.LoadDirectory(dataDirectory, path)` | Template from an explicit Data/RimUnity directory |
| `AssetLoader.Load<T>(mod, path)` | Exported object, such as a template, material, texture or shader |
| `AssetLoader.LoadDirectory<T>(dataDirectory, path)` | Typed object from an explicit directory |
| `AssetLoader.LoadBundleAsset<T>(mod, bundle, assetPath)` | Plain-text resource addressed by its retained bundle name and Unity project path |

Generic types must derive from `RimUnity.Object` and match the exported resource's actual type.

```csharp
using RimUnity.Assets;
using RimUnity;

var template = PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
var material = AssetLoader.Load<Material>(contentMod, "Materials/Glow");
var texture = AssetLoader.Load<Texture2D>(contentMod, "Textures/Noise");
var shader = AssetLoader.Load<Shader>(contentMod, "Shaders/Dissolve");
```

These paths are examples and must exist in your catalog. Automatically collected dependencies normally use internal `__assets/` paths. Export materials, textures or shaders as standalone entries with explicit logical paths if scripts need to load them directly by a stable name.

## Loading by bundle name

If you use `BundleAssetExporter` to export assets carrying Unity AssetBundle labels:

```csharp
var template = AssetLoader.LoadBundleAsset<GameObject>(
    contentMod, "myeffects", "Assets/Effects/Portal.prefab");
```

The resulting logical path is `Bundles/myeffects/Assets/Effects/Portal.prefab`. Bundle names group plain-text resources; no binary AssetBundle is read. This API cannot load an unexported AssetBundle path. The batch particle window uses a different convention: `Assets/Effects/Portal.prefab` becomes `Particles/Effects/Portal`. Always check the final catalog.

## Caching, sharing and disposal

- Catalogs and resources are cached by directory for the lifetime of the process. Repeated loads reuse dependencies.
- Prefab templates remain inactive. Instances retain the exported hierarchy, fields, references and active states.
- Do not modify or destroy shared materials, textures or templates returned by the loader. Prefer a renderer's MaterialPropertyBlock for per-instance parameters, or create and dispose of your own Material copy.
- Destroy a GameObject when it is no longer needed. Do not assume every one-shot particle effect automatically destroys its root.
- Restart the game after changing exported files. There is no automatic catalog refresh, template hot reload or public resource unloading API.

## Distributing content assets

Include `catalog.json` and all files referenced by its dependencies. Copying the entire successfully exported Data/RimUnity directory is the simplest approach. If removing historical generations, follow every file reference in the catalog and dependencies. Keeping only the newest generation can break entries that were not re-exported.

`FAILED.txt`, batch reports, `.export.lock` and temporary files are not runtime resources. Let the exporter maintain stable IDs, relative paths and dependencies instead of editing them by hand.

`Tools/Pack.ps1` packages the framework only; it does not collect content mod resources. Content authors are responsible for permission to redistribute images, fonts and models. RimUnity's MIT license does not change their licenses.

## Common loading problems

| Error | Check first |
|---|---|
| Resource not found | Correct contentMod, existing logical path, no accidental use of a source file path |
| Zero or multiple matching types | Loaded mod DLL, matching fully qualified name, duplicate definitions; register an alias before loading if needed |
| Missing dependency or file | Omitted generations or dependencies; catalog and files from different exports |
| Resource type mismatch | Whether `T` in `Load<T>` matches the exported record |
| Shader compilation or layout error | Tool and game versions, conversion diagnostics, complete GLSL and descriptor files |
| Loaded but invisible | Instance active state, position/scale, material alpha, layer/render channel, pause and emission conditions |
