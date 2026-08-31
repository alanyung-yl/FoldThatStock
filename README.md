# FoldThatStock

FoldThatStock adds functional folding and collapsing behavior to weapons and stocks that do not support it in vanilla SPT.

Version 2.0.0 introduces script-driven first-person animations for SPT 4.1.3. The mod retargets compatible animations from existing EFT weapons and synchronizes the weapon, stock, arms, wrists, hands, and fingers during folding.

[![](https://img.shields.io/github/v/release/alanyung-yl/FoldThatStock?display_name=tag&sort=semver)](https://github.com/alanyung-yl/FoldThatStock/releases/latest)
[![](https://img.shields.io/github/downloads/alanyung-yl/FoldThatStock/total)](https://github.com/alanyung-yl/FoldThatStock/releases)

## Current Behavior

- Server-side config is generated from `CreateDefaultConfig()` when missing.
- The documented release scope currently covers MCX, MPX, MCX-SPEAR, MP5, M700, SA-58, KRISS Vector, supported AK-platform, and the supported stock visual/size patches listed below.
- The client redirects supported stock bundles when matching override bundles exist.
- The client keeps `VisualStockDefinition[] BuiltInVisualStockDefinitions` as the stock/source of truth for supported visual targets.
- Visual folded state is scoped to the item view that owns the stock, not a global mod state.
- Fold operation fallback is only applied for supported FoldThatStock items.
- In-raid fold operations use stock-selected donor animation: MP5 collapse for the MP5 A3 stock, SIG Collapsing/Telescoping Stock, and MPX brace; AKS-74U left-fold for other supported SIG stocks and supported M700 stocks; and UMP right-fold for supported AK-platform weapons and the SA-58.
- KRISS Vector weapons retain EFT's native fold operation while using the replacement adapter visual.
- Repeated in-raid fold input is ignored until the active donor animation and its final pose handoff have finished.

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
- HK MP5 A3 old model stock
- M700 AI AT AICS polymer chassis
- M700 Magpul Pro 700 folding stock
- SA58 BRS stock
- SA58 folding stock
- SA58 SPR stock
- SA58 buffer tube adapter
- KRISS Vector non-folding stock adapter

## Supported Weapons

- SIG MCX .300 Blackout assault rifle
- SIG MPX 9x19 submachine gun
- SIG MCX-SPEAR 6.8x51 assault rifle
- Kalashnikov AK-74N 5.45x39 assault rifle
- Kalashnikov AK-74 5.45x39 assault rifle
- Kalashnikov AKM 7.62x39 assault rifle
- Kalashnikov AKMN 7.62x39 assault rifle
- Molot Arms VPO-136 Vepr-KM 7.62x39 carbine
- Molot Arms VPO-209 .366 TKM carbine
- Rifle Dynamics RD-704 7.62x39 assault rifle
- HK MP5 Navy 3 9x19 submachine gun
- Remington Model 700 7.62x51 bolt-action sniper rifle
- DS Arms SA-58 7.62x51 assault rifle
- TDI KRISS Vector Gen.2 .45 ACP submachine gun
- TDI KRISS Vector Gen.2 9x19 submachine gun

## Default Server Template Patches

- Weapon `5fbcc1d9016cce60e8341ab3` (`weapon_sig_mcx_gen1_762x35`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `58948c8e86f77409493f7266` (`weapon_sig_mpx_9x19`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `65290f395ae2ae97b80fdf2d` (`weapon_sig_mcx_spear_68x51`): `Foldable=true`, `FoldedSlot=mod_stock_000`
- Weapon `5644bd2b4bdc2d3b4c8b4572` (`weapon_ak74n_545x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5bf3e03b0db834001d2c4a9c` (`weapon_ak74_545x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59d6088586f774275f37482f` (`weapon_akm_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5a0ec13bfcdbcb00165aa685` (`weapon_akmn_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59e6152586f77473dc057aa1` (`weapon_vpo136_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59e6687d86f77411d949b251` (`weapon_vpo209_366tkm`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `628a60ae6b1d481ff772e9c8` (`weapon_rd704_762x39`): `Foldable=true`, `FoldedSlot=mod_stock_000`
- Weapon `5b0bbe4e5acfc40dc528a72d` (`weapon_dsa_sa58_762x51`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5926bb2186f7744b1c6c6e60` (`weapon_hk_mp5_navy3_9x19`): `Foldable=true`, `FoldedSlot=""`, `SizeReduceRight=1`
- Weapon `5bfea6e90db834001b7347f3` (`weapon_remington_m700_762x51`): `Foldable=true`, `FoldedSlot=mod_stock`
- Stock `5fbcc437d724d907e2077d5c` (`stock_all_sig_thin_folding_stock`): `SizeReduceRight=1`
- Stock `58ac1bf086f77420ed183f9f` (`stock_all_sig_folding_knuckle`): `SizeReduceRight=1`
- Stock `5c5db6f82e2216003a0fe914` (`stock_mpx_pmm_ulss`): `SizeReduceRight=1`
- Stock `5fbcc429900b1d5091531dd7` (`stock_all_sig_telescoping_stock`): `SizeReduceRight=1`
- Stock `5894a13e86f7742405482982` (`stock_all_sig_mpx_mcx_early_type`): `SizeReduceRight=1`
- Stock `6761496fe2cf1419500357e9` (`stock_all_sig_mpx_brace`): `SizeReduceRight=1`
- Stock `6529348224cbe3c74a05e5c4` (`stock_all_sig_stock_locking_hinge_assembly`): `SizeReduceRight=1`
- Stock `5649b2314bdc2d79388b4576` (`stock_ak_utg_sfs_adapter`): `SizeReduceRight=1`
- Stock `5b0e794b5acfc47a877359b2` (`stock_ak_magpul_zhukov_s`): `SizeReduceRight=1`
- Stock `5926d40686f7740f152b6b7e` (`stock_mp5_hk_a3_std`): `SizeReduceRight=1`
- Stock `5d25d0ac8abbc3054f3e61f7` (`stock_m700_ai_at_aics_chasiss`): `SizeReduceRight=1`
- Stock `5cdeac42d7f00c000d36ba73` (`stock_m700_magpul_pro_700_folding_stock`): `SizeReduceRight=1`
- Stock `5b7d64555acfc4001876c8e2` (`stock_sa58_ds_arms_para_brs`): `SizeReduceRight=1`, `BlocksFolding=false`
- Stock `5b7d63cf5acfc4001876c8df` (`stock_sa58_ds_arms_para_folding_stock`): `SizeReduceRight=1`, `BlocksFolding=false`
- Stock `5b7d63de5acfc400170e2f8d` (`stock_sa58_ds_arms_para_spr_stock`): `SizeReduceRight=1`, `BlocksFolding=false`
- Stock `5b099bf25acfc4001637e683` (`stock_sa58_ds_arms_para_folding_buffer_tube_adapter`): `SizeReduceRight=1`, `BlocksFolding=false`
- Stock `5fb655b748c711690e3a8d5a` (`stock_vector_kriss_non_folding_adapter`): `SizeReduceRight=1`, `BlocksFolding=false`

## Installation And Updates

1. Extract the release archive directly into your SPT installation folder.
2. If Windows asks about existing files while updating, choose to replace all files.
3. Start SPT normally.

Existing server configs retain their settings. Newly supported built-in weapon and stock entries are appended automatically by template id; an existing disabled entry remains disabled.

## Troubleshooting

If a supported weapon still folds logically but the donor hand animation does not play and the mod falls back to the old no-animation behavior:

1. Exit the game.
2. Open the SPT Launcher settings and select **Clear Temp Files**.
3. Start the game again and retest folding and unfolding.

This cleared the issue in one observed test session. It is not yet known whether the problem is an isolated stale-cache condition or a reproducible loading issue.

## Notes

- Client builds deploy the DLL but preserve live stock bundles by default. Pass `-p:DeployBundlesToSPT=true` only when the repository bundle files are intentionally ready to replace the live copies.
- If you do not want a supported stock visual override, remove that stock's bundle file from `BepInEx\plugins\FoldThatStock\`.
- Removing a bundle disables that stock's custom visual override, but it does not remove any server-side weapon or stock patch already enabled

## Current Limitations

- Donor hand contact is retargeted at runtime, so exact palm-to-stock contact can vary with stock geometry.
- Some vanilla stocks that should be foldable are still yet to be supported
- Support is currently limited to the stock bundles included in this release

## Roadmap

- Continue expanding stock coverage and animation/visual polish.

## License

FoldThatStock is licensed under the [GNU General Public License v3.0](LICENSE).
