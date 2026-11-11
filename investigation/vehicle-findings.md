# Exact-build vehicle ESP data

Target: PUBG Mobile Global 4.6.0 (21525), ARM64; same libUE4 identity recorded in the parent investigation. All investigation operations were read-only reflection or bounded memory reads.

## Reflected fields and chains

The observed Buggy class chain is `BP_VH_Buggy_2_C -> BP_VH_Buggy_C -> BP_STExtraWheeledVehicle_C -> STExtraWheeledVehicle -> STExtraVehicleBase -> Pawn -> Actor -> Object`. The common base family was independently checked on 17 current training vehicles, including wheeled vehicles, tracked MTLB, and a motorglider.

| Value | Address rule | Reflected owner/type |
|---|---|---|
| Vehicle common component | `qword[vehicle+0xC08]` | STExtraVehicleBase.VehicleCommon, ObjectProperty |
| Maximum HP | `float[common+0x350]` | VehicleCommonComponent.HPMax, FloatProperty |
| Current HP | `float[common+0x354]` | VehicleCommonComponent.HP, FloatProperty |
| Maximum fuel | `float[common+0x438]` | VehicleCommonComponent.FuelMax, FloatProperty |
| Current fuel | `float[common+0x43C]` | VehicleCommonComponent.Fuel, FloatProperty |
| Vehicle type | **one byte** at `vehicle+0x70C` | STExtraVehicleBase.VehicleType, EnumProperty backed by ByteProperty |
| Display text | FText at `vehicle+0x9A0` | STExtraVehicleBase.VehicleDisplayName, TextProperty |
| Position | root component +0x208, RelativeLocation +0x1E4 | Inherited Actor / SceneComponent fields |

Evidence: full inherited Buggy fields in `vehicle-reflection-buggy.json`; component fields in `vehicle-component-VehicleCommon.json`; live values and class chains in `vehicle-live-samples.json`.

Use finite bounded HP/Fuel values and positive maxima; percentages are current/max*100. Fuel values are not already percentages and are not measured on a fixed 100-unit scale.

## Selected live samples

| Vehicle class | HP / maximum | Fuel / maximum |
|---|---:|---:|
| BP_VH_Buggy_2_C | 1171.451 / 1550 | 2500000 / 2500000 |
| VH_Dacia_C | 1800 / 1800 | 2750000 / 2750000 |
| VH_UAZ01_C | 2450 / 2450 | 3000000 / 3000000 |
| VH_UTV_C | 273.499 / 2000 | 2500000 / 2500000 |
| VH_MTLB_C | 2380 / 2400 | 4000000 / 4000000 |
| VH_Motorglider_C | 836.287 / 1000 | 2347867.5 / 2500000 |

The motorglider provides an actual non-full-fuel sample (~93.91%). The UTV and Buggy provide damaged-vehicle HP samples. No writes or induced vehicle damage/fuel consumption were performed.

## Vehicle labels and enum evidence

`VehicleType` is explicitly byte-backed; treating its four adjacent bytes as one integer would combine unrelated neighboring data. `vehicle-type-metadata.json` retains the live underlying property class and element size. The referenced UEnum `ESTExtraVehicleType` contains 167 declarations. Its entire declared mapping, with correct FName numeric suffix reconstruction, is in `vehicle-type-enum-complete.json`.

Selected mappings: 13 Buggy_1, 5 Dacia_0, 9 UAZ_0, 58 Motorglider, 65 ATV, 108 UTV, 154 MTLB. Snowmobile and Snowbike objects currently store166 (`VT_MAX`), so a validated class-name fallback is needed for unknown/sentinel values. Declared enum variants are not all claimed to have been instantiated in the current world.

The observed VehicleDisplayName FText strings contain numeric localization keys, for example Buggy `34235`, Dacia `34220`, UAZ `34234`; they are not the friendly English vehicle names. Do not present these keys as useful display names. Labels can use the verified type enum plus class fallback unless the game's localization lookup is independently decoded.

The exploratory `display_string_candidates` in `vehicle-live-samples.json` are deliberately labeled candidates: arbitrary adjacent readable UTF-16 bytes must not be treated as field semantics. The established localization-key observation comes from known FText data and shared display string paths; no generic FText decoder is supplied as a production contract.
