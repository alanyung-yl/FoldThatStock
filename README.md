# FoldThatStock

FoldThatStock adds functional folding and collapsing behavior to weapons and stocks that do not support it in vanilla SPT. Its server patch enables the required behavior on supported item templates, while its client plugin supplies the missing stock visuals and first-person animation.

Version 1.1.0 brings the major folding-animation update introduced in FoldThatStock 2.0.0, together with the expanded v2.1.0 weapon and stock support, back to SPT 4.0.13. Unlike earlier v1.x releases, supported in-raid fold operations now retarget animations from compatible EFT weapons and synchronize the weapon, stock, arms, wrists, hands, and fingers instead of changing only the logical and stock visual state.

FoldThatStock v1.x.x releases target SPT 4.0.x. FoldThatStock v2.x.x releases target SPT 4.1.x.

[![](https://img.shields.io/github/v/release/alanyung-yl/FoldThatStock?display_name=tag&sort=semver)](https://github.com/alanyung-yl/FoldThatStock/releases/latest)
[![](https://img.shields.io/github/downloads/alanyung-yl/FoldThatStock/total)](https://github.com/alanyung-yl/FoldThatStock/releases)

## Current Behavior

- Server-side config is generated from `CreateDefaultConfig()` when missing.
- The documented release scope currently covers MCX, MPX, MCX-SPEAR, UZI PRO, MP5, M700, AXMC, SA-58, KRISS Vector, supported AK-platform, and the supported stock visual/size patches listed below.
- The client redirects supported stock bundles when matching override bundles exist.
- The client keeps `VisualStockDefinition[] BuiltInVisualStockDefinitions` as the stock/source of truth for supported visual targets.
- Visual folded state is scoped to the item view that owns the stock, not a global mod state.
- Fold operation fallback is only applied for supported FoldThatStock items.
- In-raid fold operations use stock-selected donor animation: MP5 collapse for the MP5 A3 stock, SIG Collapsing/Telescoping Stock, and MPX brace; AKS-74U left-fold for other supported SIG stocks and supported M700 stocks; UZI PRO SMG right-fold for the UZI PRO A3 brace on SIG weapons, UAS SKS stock, UAS AK stock, and other supported right-folding AK-platform stocks; and UMP right-fold for the SA-58.
- KRISS Vector weapons retain EFT's native fold operation. UZI PRO SB, SBR, and A3 stocks retain the UZI operation; the A3 final pose is corrected with the positive-Z Euler rotation.
- The UZI PRO A3 Rear Stock Adapter and CSM stock adapter retain their stock compatibility filters. Configurable host-aware suppression prevents selected adapter-mounted SIG stocks from folding on UZI PRO weapons without changing attachment compatibility or their behavior on SIG weapons.
- Repeated in-raid fold input is ignored until the active donor animation and its final pose handoff have finished.

## Supported Stocks

- SIG Sauer Thin Side-Folding Stock
- SIG Sauer Folding Knuckle Stock Adapter
- MPX/MCX PMM ULSS stock
- SIG Sauer Telescoping/Folding Stock
- SIG Sauer Collapsing/Telescoping Stock
- SB Tactical MPX Pistol Stabilizing Brace
- SIG Sauer Locking Stock Hinge Assembly
- UZI PRO A3 Tactical Modular Folding Brace
- UZI PRO Stabilizing Brace
- UZI PRO SBR buttstock
- UZI PRO A3 Tactical Rear Stock Adapter
- UZI PRO CSM stock adapter
- AKM/AK-74 ME4 buffer tube adapter
- AKM/AK-74 Magpul Zhukov-S stock
- FAB Defense UAS AK stock
- FAB Defense UAS SKS stock
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
- IWI UZI PRO pistol 9x19
- IWI UZI PRO SMG 9x19 submachine gun
- Kalashnikov AK-74N 5.45x39 assault rifle
- Kalashnikov AK-74 5.45x39 assault rifle
- Kalashnikov AKM 7.62x39 assault rifle
- Kalashnikov AKMN 7.62x39 assault rifle
- Molot Arms VPO-136 Vepr-KM 7.62x39 carbine
- Molot Arms VPO-209 .366 TKM carbine
- Rifle Dynamics RD-704 7.62x39 assault rifle
- Aklys Defense Velociraptor .300 Blackout assault rifle
- HK MP5 Navy 3 9x19 submachine gun
- Remington Model 700 7.62x51 bolt-action sniper rifle
- Accuracy International AXMC .338 LM bolt-action sniper rifle
- Simonov SKS 7.62x39 carbine
- Molot Arms Simonov OP-SKS 7.62x39 carbine
- DS Arms SA-58 7.62x51 assault rifle
- TDI KRISS Vector Gen.2 .45 ACP submachine gun
- TDI KRISS Vector Gen.2 9x19 submachine gun

## Default Server Template Patches

- Weapon `5fbcc1d9016cce60e8341ab3` (`weapon_sig_mcx_gen1_762x35`): `Foldable=true`, `FoldedSlot=mod_stock`;
- Weapon `58948c8e86f77409493f7266` (`weapon_sig_mpx_9x19`): `Foldable=true`, `FoldedSlot=mod_stock`;
- Weapon `65290f395ae2ae97b80fdf2d` (`weapon_sig_mcx_spear_68x51`): `Foldable=true`, `FoldedSlot=mod_stock_000`;
- Weapon `6680304edadb7aa61d00cef0` (`weapon_iwi_uzi_pro_pistol_9x19`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `668e71a8dadf42204c032ce1` (`weapon_iwi_uzi_pro_smg_9x19`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5644bd2b4bdc2d3b4c8b4572` (`weapon_ak74n_545x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5bf3e03b0db834001d2c4a9c` (`weapon_ak74_545x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59d6088586f774275f37482f` (`weapon_akm_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5a0ec13bfcdbcb00165aa685` (`weapon_akmn_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59e6152586f77473dc057aa1` (`weapon_vpo136_762x39`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `59e6687d86f77411d949b251` (`weapon_vpo209_366tkm`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `628a60ae6b1d481ff772e9c8` (`weapon_rd704_762x39`): `Foldable=true`, `FoldedSlot=mod_stock_000`
- Weapon `674d6121c09f69dfb201a888` (`weapon_aklys_defense_velociraptor_762x35`): `Foldable=true`, `FoldedSlot=mod_stock_000`
- Weapon `5b0bbe4e5acfc40dc528a72d` (`weapon_dsa_sa58_762x51`): `Foldable=true`, `FoldedSlot=mod_stock`
- Weapon `5926bb2186f7744b1c6c6e60` (`weapon_hk_mp5_navy3_9x19`): `Foldable=true`, `FoldedSlot=""`, `SizeReduceRight=1`
- Weapon `627e14b21713922ded6f2c15` (`weapon_accuracy_inernational_axmc_86x70`): `Foldable=true`, `FoldedSlot=""`, `SizeReduceRight=1`
- Weapon `5bfea6e90db834001b7347f3` (`weapon_remington_m700_762x51`): `Foldable=true`, `FoldedSlot=mod_stock`
- Stock `5fbcc437d724d907e2077d5c` (`stock_all_sig_thin_folding_stock`): `SizeReduceRight=1`
- Stock `58ac1bf086f77420ed183f9f` (`stock_all_sig_folding_knuckle`): `SizeReduceRight=1`
- Stock `5c5db6f82e2216003a0fe914` (`stock_mpx_pmm_ulss`): `SizeReduceRight=1`
- Stock `5fbcc429900b1d5091531dd7` (`stock_all_sig_telescoping_stock`): `SizeReduceRight=1`
- Stock `5894a13e86f7742405482982` (`stock_all_sig_mpx_mcx_early_type`): `SizeReduceRight=1`
- Stock `6761496fe2cf1419500357e9` (`stock_all_sig_mpx_brace`): `SizeReduceRight=1`
- Stock `6529348224cbe3c74a05e5c4` (`stock_all_sig_stock_locking_hinge_assembly`): `SizeReduceRight=1`
- Stock `6686717ffb75ee4a5e02eb19` (`stock_uzi_pro_a3_tactical_modular_folding_brace`): `SizeReduceRight=1`
- Stock `668032ba74b8f2050c0b917d` (`stock_uzi_pro_sb_tactical_stabilizing_brace`): `SizeReduceRight=1`
- Stock `66867310f3734a938b077f79` (`stock_uzi_pro_iwi_pro_buttstock`): `SizeReduceRight=1`
- Stock `668672b8c99550c6fd0f0b29` (`stock_uzi_pro_a3_tactical_rear_stock_adapter`): `BlocksFolding=false`
- Stock `669cf78806768ff39504fc1c` (`stock_uzi_pro_csm_rear_rail_adapter`): `BlocksFolding=false`
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

1. If you previously customized `SPT\user\mods\FoldThatStock\config.json`, back it up as a reference.
2. Extract the release archive directly into your SPT installation folder.
3. Replace all existing FoldThatStock files, including `config.json`.
4. Reapply any desired custom settings to the newly installed config, then start SPT normally.

FoldThatStock does not currently version its config schema. Updates may add fields or change existing weapon and stock values such as `FoldedSlot`, `SizeReduceRight`, and `AdditionalCompatibleStockTemplateIds`. Replacing the complete config ensures that the installed settings match the current release; retaining an older config is not the supported update path.

## Configuration and Customization

The server configuration is located at `SPT\user\mods\FoldThatStock\config.json`.

- Set the top-level `"Enabled"` value to `false` to disable all FoldThatStock server template patches.
- Set `"Enabled": false` on an individual weapon or stock entry to skip that entry's direct server template patch. This does not unload the client plugin or remove its built-in visual definition.
- Remove a stock bundle from `BepInEx\plugins\FoldThatStock\` to stop using that custom visual override. The original EFT bundle will load instead, while any enabled server patch remains active.
- Restart the server and game after changing the configuration or installed bundles.

### UZI PRO Adapter Fold Suppression

These settings prevent adapter-mounted SIG stocks from folding into the UZI PRO’s receiver or charging handle and causing visual clipping:

```json
"UziAdapterFoldSuppression": {
  "SuppressLeftFoldingStocks": true,
  "SuppressCollapsingStocks": true
}
```

- An empty adapter cannot fold because no stock is installed.
- The A3 brace and direct UZI PRO SB/SBR stocks remain unaffected.
- Each option suppresses its named adapter-mounted stock category when set to `true`. Set it to `false` to permit folding.
- Suppression applies only on UZI PRO hosts. The same stocks retain their folding behavior on SIG weapons.
- Unfolding is always allowed, and no attachments or compatibility filters are removed.

## Troubleshooting

If a supported weapon still folds logically but the donor hand animation does not play and the mod falls back to the old no-animation behavior:

1. Exit the game.
2. Open the SPT Launcher settings and select **Clear Temp Files**.
3. Start the game again and retest folding and unfolding.

This cleared the issue in one observed test session. It is not yet known whether the problem is an isolated stale-cache condition or a reproducible loading issue.

## Development Note

Client builds deploy the DLL but preserve live stock bundles by default. Pass `-p:DeployBundlesToSPT=true` only when the repository bundle files are intentionally ready to replace the live copies.

## Current Limitations

All currently identified vanilla weapons and stocks requiring folding corrections are covered. Remaining limitations are:

- Donor hand contact is retargeted at runtime, so exact palm-to-stock contact can vary with stock geometry.
- Stocks from other mods are not supported. New items introduced in future EFT updates will be added in subsequent mod updates.

## Roadmap

- Maintain compatibility with future SPT and EFT releases.
- Add support for newly introduced or reported folding gaps.
- Continue improving donor hand contact and visual polish.

## License

FoldThatStock is licensed under the [GNU General Public License v3.0](LICENSE).
