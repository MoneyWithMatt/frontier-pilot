# Prototype Base Lodge house fallback — 2026-10-02

Live testing rejected the previous `MICROSOFT_ASSET_FieldCabins_01` placeholder, GUID `{7C24C83A-0588-4208-9B89-6F01B7D9B82F}`, because MSFS displayed a large compound/hangar-like object rather than a small lodge.

The installed MSFS 2024 ModelLib cache was inspected read-only. `fs-base-scenery.dat` maps the standalone building name `BF_TemplateHouse01` to library GUID `{0286FC19-4558-4228-A480-2CD5690A2022}`. The cache SHA-256 was `236000608E00326162FA7E2B7A313765035165394B0B6F9BDE46B37967655BB6`. The corresponding installed streamed package index `fs20-fs-base-scenery/content/minimal.fsarchive` had SHA-256 `C4C48211DD87B884D4CA28C6F429D9ED5C915F7DCAA4FD4F7DFA6A58AB0565CD`.

Frontier continues to store `prototype-base-lodge-v0` in saved layouts and resolves that stable ID to the new library GUID during generation. Existing sites therefore require only a rebuild and reinstall. No Microsoft model or texture is copied into the generated package.

The replacement uses normal scale and a provisional 10 × 8 m editor footprint. Its exact bounds, appearance, ground contact and orientation are not established by catalogue inspection and require a live simulator acceptance test. It remains temporary pending a Frontier-owned lodge model.
