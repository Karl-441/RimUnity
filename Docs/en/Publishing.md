# Plain-text asset build manager

[简体中文](../Publishing.md) | [English](Publishing.md)

Unity menu: `RimUnity / 明文资源构建管理器` (Plain-text asset build manager). When installed as a local UPM package, the window uses the package source directly. There is no need to copy scripts into Assets.

Configuration is stored in the authoring project's `ProjectSettings/RimUnity.json`. Selection and UI flags are stored in `ProjectSettings/RimUnityWindow.json`. No configuration file is required on first launch. In `输出与工具配置` (Output and tool configuration), set the content repository, ShaderTool, game directory and destination mod roots for AssetBundle labels, then save. Keep authoring-project configuration out of the RimUnity framework repository.

Entries come from configured AssetBundle labels and manually added assets. Texture, material, shader and animation dependencies are collected automatically. Output goes to `1.7/Data/RimUnity` beneath the target mod root. Reports are stored in the authoring project's `Library/RimUnity/Reports`.

A full rebuild contains only the entries currently selected. It replaces existing data after every target completes export and structural validation. Use incremental mode to retain other entries. Failed checks, failed exports and cancellation during preparation do not replace published data. A failure during publication restores the previous version. Structural validation does not establish visual correctness in the game.

Missing scripts are recorded as warnings in the prefab and reported when the game loads it. Other exportable components remain usable. Shader Graph and script assets requiring separate compilation are identified in the list; deselect those entries to export the remaining content.
