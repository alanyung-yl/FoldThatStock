# FoldThatStock

Fresh implementation of the client plugin and SPT server patcher.

The server patch enables foldable behavior on configured vanilla templates, and the client plugin handles the missing visual animation by binding a per-item stock visual controller to supported stock views.

[![](https://img.shields.io/github/v/release/alanyung-yl/FoldThatStock?display_name=tag&sort=semver)](https://github.com/alanyung-yl/FoldThatStock/releases/latest)
[![](https://img.shields.io/github/downloads/alanyung-yl/FoldThatStock/total)](https://github.com/alanyung-yl/FoldThatStock/releases)

## Current Behavior

- Server-side config is generated from `CreateDefaultConfig()` when missing.
- The documented release scope currently covers MCX, MPX, supported AK-platform, and the supported stock visual/size patches listed below.
- The client redirects supported stock bundles when matching override bundles exist.
- The client keeps `VisualStockDefinition[] BuiltInVisualStockDefinitions` as the stock/source of truth for supported visual targets.
- The SIG thin stock folded quaternion is preserved as `X=0, Y=0.7071068, Z=0.7071068, W=0`.
- Visual folded state is scoped to the item view that owns the stock, not a global mod state.
- Fold operation fallback is only applied for supported FoldThatStock items.

## Supported Stocks

- SIG Sauer Thin Side-Folding Stock
- SIG Sauer Folding Knuckle Stock Adapter
- MPX/MCX PMM ULSS stock
- SIG Sauer Telescoping/Folding Stock
- SIG Sauer Collapsing/Telescoping Stock
- SB Tactical MPX Pistol Stabilizing Brace
- SIG Sauer Locking Stock Hinge Assembly
- AKM/AK-74 ME4 buffer tube adapter
- AKM/AK-74 Magpul Zhukov-S stock

## Supported Weapons

- SIG MCX .300 Blackout assault rifle
- SIG MPX 9x19 submachine gun
- Kalashnikov AK-74N 5.45x39 assault rifle
- Kalashnikov AK-74 5.45x39 assault rifle
- Kalashnikov AKM 7.62x39 assault rifle
- Kalashnikov AKMN 7.62x39 assault rifle
- Molot Arms VPO-136 Vepr-KM 7.62x39 carbine
- Molot Arms VPO-209 .366 TKM carbine
- Rifle Dynamics RD-704 7.62x39 assault rifle

## Default Server Template Patches

- Weapon `5fbcc1d9016cce60e8341ab3` (`weapon_sig_mcx_gen1_762x35`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `58948c8e86f77409493f7266` (`weapon_sig_mpx_9x19`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5644bd2b4bdc2d3b4c8b4572` (`weapon_ak74n_545x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5bf3e03b0db834001d2c4a9c` (`weapon_ak74_545x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59d6088586f774275f37482f` (`weapon_akm_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5a0ec13bfcdbcb00165aa685` (`weapon_akmn_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59e6152586f77473dc057aa1` (`weapon_vpo136_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59e6687d86f77411d949b251` (`weapon_vpo209_366tkm`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `628a60ae6b1d481ff772e9c8` (`weapon_rd704_762x39`): `Foldable=true`, `FoldedSlot=mod_stock_000`
- Stock `5fbcc437d724d907e2077d5c` (`stock_all_sig_thin_folding_stock`): `SizeReduceRight=1`
- Stock `58ac1bf086f77420ed183f9f` (`stock_all_sig_folding_knuckle`): `SizeReduceRight=1`
- Stock `5c5db6f82e2216003a0fe914` (`stock_mpx_pmm_ulss`): `SizeReduceRight=1`
- Stock `5fbcc429900b1d5091531dd7` (`stock_all_sig_telescoping_stock`): `SizeReduceRight=1`
- Stock `5894a13e86f7742405482982` (`stock_all_sig_mpx_mcx_early_type`): `SizeReduceRight=1`
- Stock `6761496fe2cf1419500357e9` (`stock_all_sig_mpx_brace`): `SizeReduceRight=1`
- Stock `6529348224cbe3c74a05e5c4` (`stock_all_sig_stock_locking_hinge_assembly`): `SizeReduceRight=1`
- Stock `5649b2314bdc2d79388b4576` (`stock_ak_utg_sfs_adapter`): `SizeReduceRight=1`
- Stock `5b0e794b5acfc47a877359b2` (`stock_ak_magpul_zhukov_s`): `SizeReduceRight=1`

## Installation And Updates

1. Extract the release archive directly into your SPT installation folder.
2. If Windows asks about existing files while updating, choose to replace all files.
3. Start SPT normally.

The release includes the current `SPT\user\mods\FoldThatStock\config.json`; choosing replace all updates it. If you intentionally skip replacing that file, new default stock and weapon entries are not merged automatically.

## Notes

- If you do not want a supported stock visual override, remove that stock's bundle file from `BepInEx\plugins\FoldThatStock\`.
- Removing a bundle disables that stock's custom visual override, but it does not remove any server-side weapon or stock patch already enabled

## Current Limitations

- Player fold/unfold animation is not implemented yet
- Some vanilla stocks that should be foldable are still yet to be supported
- Support is currently limited to the stock bundles included in this release

## Roadmap

- Continue expanding stock coverage, visual polish, and player animation support.
