# FoldThatStock

Fresh implementation of the client plugin and SPT server patcher.

The server patch enables foldable behavior on configured vanilla templates, and the client plugin handles the missing visual animation by binding a per-item stock visual controller to supported stock views.

## Current Behavior

- Server-side config is generated from `CreateDefaultConfig()` when missing.
- The documented release scope currently covers MCX, MPX, and supported SIG-family stock visual overrides.
- The client redirects supported stock bundles when matching override bundles exist.
- The client keeps `VisualStockDefinition[] BuiltInVisualStockDefinitions` as the stock/source of truth for supported visual targets.
- The SIG thin stock folded quaternion is preserved as `X=0, Y=0.7071068, Z=0.7071068, W=0`.
- Visual folded state is scoped to the item view that owns the stock, not a global mod state.
- Fold operation fallback is only applied for supported FoldThatStock items.

## Default Server Template Patches

- Weapon `5fbcc1d9016cce60e8341ab3` (`weapon_sig_mcx_gen1_762x35`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `58948c8e86f77409493f7266` (`weapon_sig_mpx_9x19`): `Foldable=true`, `FoldedSlot=mod_stock`
- Stock `5fbcc437d724d907e2077d5c` (`stock_all_sig_thin_folding_stock`): `SizeReduceRight=1`
- Stock `58ac1bf086f77420ed183f9f` (`stock_all_sig_folding_knuckle`): `SizeReduceRight=1`
- Stock `5c5db6f82e2216003a0fe914` (`stock_mpx_pmm_ulss`): `SizeReduceRight=1`
- Stock `5fbcc429900b1d5091531dd7` (`stock_all_sig_telescoping_stock`): `SizeReduceRight=1`
- Stock `6529348224cbe3c74a05e5c4` (`stock_all_sig_stock_locking_hinge_assembly`): `SizeReduceRight=1`

## Notes

- If `SPT.Server` is running, the server deploy target may fail because `FoldThatStockServer.dll` is locked.
- Bundle overrides are copied from `bundle\*.bundle` into `plugin\FoldThatStock\` during client builds.
- If you do not want a supported stock visual override, remove that stock's bundle file from `BepInEx\plugins\FoldThatStock\`.
- Removing a bundle disables that stock's custom visual override, but it does not remove any server-side weapon or stock patch already enabled
- Add future supported stock visuals in `BuiltInVisualStockDefinitions`.
- Add future server template patches in `CreateDefaultConfig()` or in the generated server `config.json`.

## Current Limitations

- Player fold/unfold animation is not implemented yet
- Some vanilla stocks that should be foldable are still yet to be supported
- Support is currently limited to the stock bundles included in this release

## Roadmap

- AK/ME4 adapter support is paused until the custom bundle material and ready.
