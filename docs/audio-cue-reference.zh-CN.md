# 音效 Cue 参考表

本文用于给 FishSlapper 的跳水扇鱼机制做音效调试参考。

## 结论

- 本机 `Content/Data/AudioChanges.xnb` 可以正常读取，但当前版本导出结果为空表。
- 这意味着大量原版 cue 并不通过 `AudioChanges` 数据表暴露，而是内建在 `Content/XACT/Sound Bank.xsb` 里。
- 本文下面的“完整 cue 名清单”来自对本机 [Sound Bank.xsb](/C:/Program Files (x86)/Steam/steamapps/common/Stardew Valley/Content/XACT/Sound Bank.xsb) 的字符串提取。
- 清单总数为 `433`，已剔除显然不是 cue 的 `Sound Bank` / `Wave Bank` 两项。
- 这份清单同时包含音乐、环境循环声、普通音效、别名，以及少量可能已废弃或仅内部使用的名称；并不等于“全部都适合 `Game1.playSound(...)` 当作瞬时音效”。

## 当前代码使用

| 用途 | Cue | 位置 | 说明 |
| --- | --- | --- | --- |
| 扇鱼命中 | `iwyxdxl.FishSlapper_SlapSound` | [ModConstants.cs](/C:/Users/3DBox/Documents/CodeForFun/StardewValley_FishSlapper_Mod/FishSlapper/ModConstants.cs#L5) | mod 自定义，资源为 `assets/slap.wav` |
| 角色跳水起跳 | `dwop` | [ModConstants.cs](/C:/Users/3DBox/Documents/CodeForFun/StardewValley_FishSlapper_Mod/FishSlapper/ModConstants.cs#L7) | 原版跳跃感最强的瞬时声 |
| 角色入水 | `iwyxdxl.FishSlapper_PlayerDive` | [ModConstants.cs](/C:/Users/3DBox/Documents/CodeForFun/StardewValley_FishSlapper_Mod/FishSlapper/ModConstants.cs#L6) | mod 自定义，资源为 `assets/PlayerDive.wav` |
| 角色出水 | `pullItemFromWater` | [ModConstants.cs](/C:/Users/3DBox/Documents/CodeForFun/StardewValley_FishSlapper_Mod/FishSlapper/ModConstants.cs#L10) | 当前回岸出水声 |
| 鱼出水 / 入水 | `dwop` | [ModConstants.cs](/C:/Users/3DBox/Documents/CodeForFun/StardewValley_FishSlapper_Mod/FishSlapper/ModConstants.cs#L8) | 当前鱼体跃出和落回水面的统一瞬时声 |
| 自定义音频注册 | `iwyxdxl.FishSlapper_PlayerDive` | [ModEntry.cs](/C:/Users/3DBox/Documents/CodeForFun/StardewValley_FishSlapper_Mod/FishSlapper/ModEntry.cs#L121) | 注入到 `Data/AudioChanges` |
| 扇鱼成功结算 | `jingle1` | [VanillaFishingBridge.cs](/C:/Users/3DBox/Documents/CodeForFun/StardewValley_FishSlapper_Mod/FishSlapper/Vanilla/VanillaFishingBridge.cs#L133) | 沿用原版钓鱼成功提示 |
| 扇鱼失败结算 | `fishEscape` | [VanillaFishingBridge.cs](/C:/Users/3DBox/Documents/CodeForFun/StardewValley_FishSlapper_Mod/FishSlapper/Vanilla/VanillaFishingBridge.cs#L144) | 沿用原版逃鱼提示 |

## 进一步排查

- 若后续还要继续排查原版音效，优先配合 `debug logSounds` 记录实际播放的 cue。
- 本文这份完整清单更适合“先缩小候选范围”，再回到代码里替换验证。

## 完整 Cue 名清单

以下名称来自本机 `Sound Bank.xsb` 提取，按首字母分组。

### 0-9
- `50s`

### A
- `AbigailFlute`
- `AbigailFluteDuet`
- `achievement`
- `aerobics`
- `archaeo`
- `axchop`
- `axe`

### B
- `babblingBrook`
- `backpackIN`
- `barrelBreak`
- `batFlap`
- `batScreech`
- `bigDeSelect`
- `bigDrums`
- `bigSelect`
- `bob`
- `book_read`
- `boop`
- `boulderBreak`
- `boulderCrack`
- `breakingGlass`
- `breathin`
- `breathout`
- `breezy`
- `bubbles`
- `bugLevelLoop`
- `busDoorOpen`
- `busDriveOff`
- `button_press`
- `button_tap`
- `button1`

### C
- `cacklingWitch`
- `caldera`
- `camel`
- `cameraNoise`
- `cancel`
- `cast`
- `cat`
- `cavedrip`
- `Cavern`
- `christmasTheme`
- `clam_tone`
- `clank`
- `Cloth`
- `CloudCountry`
- `clubhit`
- `clubloop`
- `clubSmash`
- `clubswipe`
- `cluck`
- `coin`
- `coldSpell`
- `communityCenter`
- `cow`
- `cowboy_boss`
- `cowboy_dead`
- `cowboy_explosion`
- `Cowboy_Footstep`
- `cowboy_gopher`
- `cowboy_gunload`
- `Cowboy_gunshot`
- `Cowboy_monsterDie`
- `cowboy_monsterhit`
- `cowboy_outlawsong`
- `Cowboy_OVERWORLD`
- `cowboy_powerup`
- `Cowboy_Secret`
- `Cowboy_singing`
- `Cowboy_undead`
- `cracklingFire`
- `crafting`
- `crane`
- `crane_game`
- `crane_game_fast`
- `crickets`
- `cricketsAmbient`
- `crit`
- `croak`
- `crow`
- `crystal`
- `Crystal Bells`
- `cursed_mannequin`
- `cut`
- `Cyclops`

### D
- `daggerswipe`
- `darkCaveLoop`
- `death`
- `debuffHit`
- `debuffSpell`
- `desolate`
- `detector`
- `Dff`
- `dialogueCharacter`
- `dialogueCharacterClose`
- `dirtyHit`
- `discoverMineral`
- `distantBanjo`
- `distantTrain`
- `dog_bark`
- `dog_pant`
- `dogs`
- `dogWhining`
- `doorClose`
- `doorCreak`
- `doorCreakReverse`
- `doorOpen`
- `dropItemInWater`
- `drumkit0`
- `drumkit1`
- `drumkit2`
- `drumkit3`
- `drumkit4`
- `drumkit5`
- `drumkit6`
- `Duck`
- `Duggy`
- `dustMeep`
- `DwarvishSentry`
- `dwoop`
- `dwop`

### E
- `EarthMine`
- `eat`
- `echos`
- `elliottPiano`
- `EmilyDance`
- `EmilyDream`
- `EmilyTheme`
- `end_credits`
- `event1`
- `event2`
- `explosion`

### F
- `fairy_heal`
- `fall_day_ambient`
- `fall1`
- `fall2`
- `fall3`
- `fallDown`
- `fallFest`
- `fastReel`
- `fieldofficeTentMusic`
- `fireball`
- `firework`
- `fishBite`
- `fishBite_alternate_0`
- `fishBite_alternate_1`
- `fishBite_alternate_2`
- `fishEscape`
- `FishHit`
- `fishingRodBend`
- `fishSlap`
- `flameSpell`
- `flameSpellHit`
- `FlowerDance`
- `flute`
- `flybuzzing`
- `frog_slap`
- `FrogCave`
- `Frost_Ambient`
- `FrostMine`
- `frozen`
- `furnace`
- `fuse`

### G
- `getNewSpecialItem`
- `ghost`
- `Ghost Synth`
- `give_gift`
- `glug`
- `goat`
- `goldenWalnut`
- `gorilla_intro`
- `grandpas_theme`
- `grassyStep`
- `grunt`
- `gulp`
- `gusviolin`

### H
- `hammer`
- `harvest`
- `harveys_theme_jazz`
- `healSound`
- `heavy`
- `heavyEngine`
- `hitEnemy`
- `hoeHit`
- `honkytonky`
- `horse_flute`
- `Hospital_Ambient`

### I
- `Icicles`
- `IslandMusic`

### J
- `jaunty`
- `jingle1`
- `jingleBell`
- `jojaOfficeSoundscape`
- `jungle_ambience`
- `junimoKart`
- `junimoKart_coin`
- `junimoKart_ghostMusic`
- `junimoKart_mushroomMusic`
- `junimoKart_slimeMusic`
- `junimoKart_whaleMusic`
- `junimoMeep1`
- `junimoStarSong`

### K
- `keyboardTyping`
- `killAnimal`
- `kindadumbautumn`

### L
- `Lava_Ambient`
- `LavaMine`
- `leafrustle`
- `libraryTheme`

### M
- `machine_bell`
- `magic_arrow`
- `magic_arrow_hit`
- `magma_sprite_die`
- `magma_sprite_hit`
- `magma_sprite_spot`
- `MainTheme`
- `Majestic`
- `MarlonsTheme`
- `marnieShop`
- `mermaidSong`
- `metal_tap`
- `Meteorite`
- `Milking`
- `minecartLoop`
- `miniharp_note`
- `money`
- `moneyDial`
- `monkey1`
- `monsterdead`
- `moonlightJellies`
- `moss_cut`
- `mouseClick`
- `movie_classic`
- `movie_nature`
- `movie_wumbus`
- `movieScreenAmbience`
- `movieTheater`
- `movieTheaterAfter`
- `musicboxsong`

### N
- `Near The Planet Core`
- `New Snow`
- `newArtifact`
- `newRecipe`
- `newRecord`
- `night_market`
- `nightTime`

### O
- `objectiveComplete`
- `ocean`
- `Of Dwarves`
- `openBox`
- `openChest`
- `Orange`
- `Ostrich`
- `Overcast`
- `owl`

### P
- `parachute`
- `parrot`
- `parrot_flap`
- `parrot_squawk`
- `parry`
- `phone`
- `Pickup_Coin15`
- `pickUpItem`
- `pig`
- `Pink Petals`
- `PIRATE_THEME`
- `PIRATE_THEME(muffled)`
- `planeflyby`
- `playful`
- `Plums`
- `pool_ambient`
- `poppy`
- `potterySmash`
- `powerup`
- `pullItemFromWater`
- `purchase`
- `purchaseClick`
- `purchaseRepeat`

### Q
- `qi_shop`
- `qi_shop_purchase`
- `questcomplete`
- `quickSlosh`

### R
- `rabbit`
- `Raccoon`
- `raccoonSong`
- `ragtime`
- `rain`
- `rainsound`
- `reward`
- `roadnoise`
- `robotBLASTOFF`
- `robotSoundEffects`
- `rockGolemDie`
- `rockGolemHit`
- `rockGolemSpawn`
- `rooster`

### S
- `sad_kid`
- `sadpiano`
- `Saloon1`
- `sam_acoustic1`
- `sam_acoustic2`
- `sampractice`
- `sandyStep`
- `sappypiano`
- `scissors`
- `seagulls`
- `Secret Gnomes`
- `secret1`
- `seeds`
- `select`
- `sell`
- `serpentDie`
- `serpentHit`
- `SettlingIn`
- `sewing_loop`
- `shadowDie`
- `shadowHit`
- `shadowpeep`
- `shaneTheme`
- `sheep`
- `shimmeringbastion`
- `shiny4`
- `Ship`
- `shwip`
- `SinWave`
- `sipTea`
- `skeletonDie`
- `skeletonHit`
- `skeletonStep`
- `slime`
- `slimedead`
- `slimeHit`
- `slingshot`
- `slosh`
- `slowReel`
- `smallSelect`
- `snowyStep`
- `spaceMusic`
- `spirits_eve`
- `spring_day_ambient`
- `spring_night_ambient`
- `spring1`
- `spring2`
- `spring3`
- `SpringBirds`
- `springsongs`
- `springtown`
- `squid_bubble`
- `squid_hit`
- `squid_move`
- `Stadium_ambient`
- `Stadium_cheer`
- `stairsdown`
- `stardrop`
- `starshoot`
- `statue_of_blessings`
- `steam`
- `stone_button`
- `stoneCrack`
- `stoneStep`
- `stumpCrack`
- `submarine_landing`
- `submarine_song`
- `summer_day_ambient`
- `summer1`
- `summer2`
- `summer3`
- `SunRoom`
- `sweet`
- `swordswipe`

### T
- `telephone_buttonPush`
- `telephone_dialtone`
- `telephone_ringingInEar`
- `terraria_boneSerpent`
- `terraria_meowmere`
- `terraria_warp`
- `throw`
- `throwDownITem`
- `thudStep`
- `thunder`
- `thunder_small`
- `ticket_machine_whir`
- `tickTock`
- `tinymusicbox`
- `tinyWhip`
- `title_night`
- `toolCharge`
- `toolSwap`
- `toyPiano`
- `trainLoop`
- `trainWhistle`
- `trashbear`
- `trashbear_flute`
- `trashcan`
- `trashcanlid`
- `treasure_totem`
- `treecrack`
- `treethud`
- `tribal`
- `Tropical Jam`
- `tropical_island_day_ambient`
- `turtle_pet`

### U
- `UFO`
- `Upper_Ambient`

### V
- `Volcano_Ambient`
- `VolcanoMines`
- `VolcanoMines1`
- `VolcanoMines2`

### W
- `wand`
- `warrior`
- `waterfall`
- `waterfall_big`
- `wateringCan`
- `waterSlosh`
- `wavy`
- `wedding`
- `weed_cut`
- `whistle`
- `wind`
- `windstorm`
- `winter_day_ambient`
- `winter1`
- `winter2`
- `winter3`
- `WizardSong`
- `woodchipper`
- `woodchipper_occasional`
- `woodsTheme`
- `woodWhack`
- `woodyHit`
- `woodyStep`

### X
- `XOR`

### Y
- `yoba`
