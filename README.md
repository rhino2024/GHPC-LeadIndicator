# 提前量指示器

Gunner, HEAT, PC! 的 MelonLoader 模组。炮手镜里按一下 `Alt` 锁住准星下的敌方载具，画绿框、拉一条线到提前量小圈，再报出距离。把准星压到圈上开火，炮弹飞完这段路，目标正好走进弹着点。也可以让它别按：队友用无线电报过的敌人，模组自动替你锁上最新报的那一辆（`LeadMode` 里二选一）。报点目标不在炮镜圆圈里时，镜内还会出现一个指向它的绿色箭头，直到它进镜。

## 你会看到什么

锁定之后，目标车体上有一个绿框，框下是距离，框和前面的小圆圈之间连一条绿线——那个小圆圈就是提前量：按游戏自己弹道表里的飞行时间算出来的落点。目标在动，圈也跟着走，每帧重算。

锁定期间会压住火控自己那份提前量：GHPC 的 FCS 是按你转镜头的角速度反推提前量的，不压掉的话，你把准星往圈上移，它会在圈上再加一份，变成双倍提前。解除锁定时原样还原。另外可以打开 `AutoRange`，锁定期间把火控距离校正到目标真实距离（没有激光测距仪的车靠这一条才能直接打中），默认关闭。

**圈跟着你炮镜上的标尺走。** 标尺（准星旁显示的 400/800…）就是火控距离，游戏按它把炮身抬起，让弹道在那个距离穿过光轴。所以拿 400 m 标尺打 1000 m 的车，圈会抬到那个标尺下真正该瞄的位置，而不是一律按真实距离画一条水平提前量。标尺等于真实距离时（激光测距过、或 `AutoRange` 改好了距离）偏移为零，圈落回车上，和以前完全一样。

**模组不会动你的炮口。** 1.7.0 把之前的自动跟踪整条去掉了：锁上之后炮塔不转、准星不跑，只画框、只画圈、只报距离；要不要往圈上压是你的操作。`Alt` 是纯粹的开关，开着的时候再按一下一律解除（不管准星下有没有车）。换目标就是按一下关、挪准星、再按一下开。报点自动锁定那套模式下 `Alt` 不生效，见下面 `LeadMode`。

只锁视野里真看得见的目标：借用游戏 AI 自己的视线判断，山后面、树线后面那类「准星方向上恰好有一辆几公里外的车」不会被锁上；锁着的时候目标躲进山背后，框也会消失。屏幕上一共只有几个像素的远车同样不锁——肉眼看不清的车，模组也不该替你锁。车体被挡住一半、只露出上半截时照样能锁：中心那条视线被挡之后，模组会再去探车体四角，任意一点能看见就算看得见。

**报点自动帮你锁一辆（`LeadMode`）。** 车长或队友用无线电报点的那些敌方载具——就是小地图上那几个报点标记——模组会取**最新那条报点**，直接替你按一次 `Alt` 把它锁上：同一个绿框、同一条绿线、同一个提前圈，同样压掉火控那份重复提前量（1.10.0 起不再替你打激光）。屏幕上始终只有一个框、一辆车，你的手不用离开鼠标。这个模式下按 `Alt` 不生效（按一下屏幕会提示怎么切回去）。不想让模组替你选目标，就把 `LeadMode` 填成 `ManualAlt`，回到「自己按 `Alt` 锁准星下的车」。数据直接读游戏自己的报点网络，队友没报就没有锁；一条上报 90 秒后过期，过期或那辆车被打掉就自动放开，转锁下一个还活着的报点。看不见的不锁：沿用上面「只锁视野里真看得见的」那套判断，另外要求车体投影落在屏幕上——圈画在视野外的目标，不值得替你压掉火控的提前量。只锁一辆是因为一门炮同一时刻只有一个弹道解算，同时锁两辆会互相抢距离和提前量补偿。

**本车火控坏了，自动锁定就放开。** 火控计算机被打掉或者没电，游戏就没有弹道解，落点、飞行时间、抬落全是猜的——这时候画出来的圈只会骗人。所以自动锁这条遇到火控故障时不锁新的、已锁的立刻解除，屏幕上提示一句，修好后自动恢复（`ManualAlt` 模式下你按 `Alt` 的行为不变）。

**1.10.0 起，报点锁不测距、不动标尺，而且咬住第一辆。** 报点自动锁不再替玩家打激光（激光会在对侧告警上亮一下，等于替你暴露一次位置），也不写火控距离——标尺完全是你自己的事，模组只画框、只画圈。它认下哪辆车就咬住哪辆：那辆车被消灭之前，后面再新的报点也不会把圈换走；它暂时被山挡住或开出镜外，框先消失，露头自己回来。清掉它的只有四种情况：被打掉、换任务、`F4` 关模组、切回 `ManualAlt`。

**报点目标不在炮镜圆圈里，就画箭头指过去（1.11.0）。** 车长或队友报过点、但那辆车不在炮镜圆圈里时，镜内沿圆圈内侧会出现一个绿色箭头指向它的方向；它一进圈箭头就消失，交给绿框那套。目标在身后时箭头贴在圆圈的左/右边缘，照着转镜头就能把它转进镜里。箭头只说明方向：不发激光、不碰火控、不碰炮塔、也不改标尺。圆圈默认取屏幕短边（整个高度），瞄具玻璃比屏幕窄就用 `SightCirclePercent` 调小；嫌箭头细就加 `SightArrowThickness`。

**步兵不画（1.11.1）。** 报点目标是步兵时，绿框、提前圈、箭头都不出现——人形目标没有提前量可言，画上去只会误导；最新一条报点是步兵时，自动退到下一条还在时效内的载具报点，而不是整层不工作。

没有激光测距仪的车，原版测距本来就用不了；锁定期间这一条替你把它补上。

`F4` 是模组总开关，屏幕上会打出当前状态。

## 装

1. 先彻底退出游戏。游戏开着的时候 dll 是锁住的，覆盖不了。
2. 把 zip 解压到游戏根目录，就是放着 `GHPC.exe` 的那一层。解压出来应该看到 `Bin\Mods\LeadIndicator.dll`。
3. 开一次游戏。MelonLoader 会在 `Bin\UserData\MelonPreferences.cfg` 里写出 `[LeadIndicator]` 这一段。

需要 MelonLoader v0.7.2（net35 运行时），下载包里没有它。它不改游戏任何文件，删掉那个 dll 就算卸载。

## 能改的东西

| 名字 | 默认 | 作用 |
| --- | --- | --- |
| Enabled | true | 总开关，关掉后按锁定键不再框目标 |
| ToggleKey | F4 | 任务中开关这个模组的按键 |
| RangeKey | LeftAlt | 炮手镜里单按：锁定/解除；Alt+数字选弹、Alt+Tab 切窗口都不会误触发 |
| ShowBox | true | 在目标车体上画绿色方框 |
| ShowLeadCircle | true | 绿线 + 提前量小圈 |
| ShowRange | true | 绿框下面显示测出的距离 |
| SuppressFcsLead | true | 锁定期间压掉火控自己的提前量，防双倍提前 |
| AutoRange | false | 锁定期间把火控距离校正到目标真实距离 |
| LockConeDegrees | 4 | 准星正前方多少度以内算指着它，越大越容易锁到旁边的车 |
| MaxRange | 6000 | 超过这个距离（米）不锁定 |
| CircleRadius | 14 | 小圈半径（1080p 像素，高分屏按比例放大） |
| BoxPadding | 4 | 绿框比车体包围盒外扩多少像素 |
| RequireLineOfSight | true | 只锁视野里真看得见的目标 |
| MinScreenRadiusPixels | 12 | 目标在屏幕上太小就不锁；填 0 关掉 |
| RespectSightRange | true | 提前圈按炮镜标尺抬落；关掉则只按真实距离画水平提前量 |
| LeadMode | AutoSpot | 提前量目标从哪来，两种模式只能选一个：`AutoSpot`＝队友报点自动锁一辆（相当于替你按一次 Alt，此时 Alt 不生效）；`ManualAlt`＝旧行为，按 Alt 锁准星下的车、报点不自动锁 |
| ShowSightArrow | true | 报点目标不在炮镜圆圈里时，镜内沿圆圈画一个指向它的绿色箭头；它进圈就消失 |
| SightCirclePercent | 100 | 箭头沿多大一个圈走：圆圈直径占屏幕短边的百分比。瞄具玻璃比屏幕窄就调小（85~95 常见） |
| SightArrowThickness | 5.5 | 箭头粗细（1080p 像素，高分屏等比放大）；嫌细往上加，3~10 都正常 |
| DebugLog | false | 出问题再开，会写 `Bin\Mods\LeadIndicator.debug.log` |

## 先说清楚

锁定期间会临时改写火控的两个内部状态（提前量 / 距离），解除时逐项还原；这是让圈准的必要条件，不是替你瞄准。炮口方向、炮塔转速、游戏 AI 全都不碰。

车体绿框量的是车体自己的包围盒，不算炮管和烟柱，车斜着停也不会宽出一圈。

## 素材

本模组不含模型与贴图：绿框、绿线、提前圈、箭头都是屏幕绘制的几何图形，用到的美术只有 GHPC 游戏本体自带的准星与瞄具，运行时由游戏加载；模组不分发任何游戏素材，也没有对任何模型做过重拓扑（retopo）处理。

## 想自己编译

不用 .NET SDK，也不用 Unity 编辑器，一个 dll 而已，用任何 Roslyn `csc` 对着游戏自带的托管程序集编就行：

```bash
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash src/build.sh deploy
```

`deploy` 会把编出来的 dll 覆盖到 `<游戏目录>\Bin\Mods\`，所以先关游戏。不加 `deploy` 就只编到 `src/bin/`。

## In English

Shows where to aim at a moving target instead of moving your gun for you. Tap `Alt` in the gunner sight to lock the enemy vehicle under the reticle: a green box appears around its hull, a line runs to a small circle marking the lead point computed from the game's own ballistic flight-time table, and the measured range is printed under the box. While locked, the mod suppresses the fire control's own lead (GHPC's FCS infers lead from your turret traverse rate, which would otherwise double it), restores it on unlock, and can optionally correct the FCS range to the true range (`AutoRange`, off by default). It only locks targets that are actually visible to the game's AI line-of-sight check and big enough on screen to be recognised; if the centre line is blocked, the hull corners are probed, so a tank showing only its upper half still locks. The lead circle follows the sight's range dial: with the dial at 400 m and a target at 1000 m, the circle rises to the point you would actually hold for that setting, and it falls back to the plain horizontal lead when the dial matches the true range (or `RespectSightRange` is off). Version 1.7.0 removed the previous auto-tracking chain entirely: the turret is never driven, the mod only draws. `Alt` is a plain on/off toggle, `F4` toggles the whole mod.

Version 1.9.0 changed what the radio reports do. Instead of drawing amber boxes around up to three reported tanks, the mod now presses the `Alt` lock for you: of the enemy vehicles your commander or squadmates have reported over the radio — the same marks the minimap uses — it takes the freshest report and locks it through the exact same path as a manual lock, so you get the one green box, the lead circle, range, suppressed FCS lead and a laser call, and there is only ever one target on screen. `LeadMode` picks which source drives the lock, and the two cannot run at once because they share the single lock: `AutoSpot` (default) locks the freshest spot report and ignores `Alt`, `ManualAlt` restores the old behaviour where you lock whatever is under your reticle. One target only, because a gun has exactly one ballistic solution at a time and two locks would fight over the FCS range and lead. A reported tank is only locked if the report is still fresh (90 s), it is genuinely visible to the game's line-of-sight check and it projects onto the screen; a tank behind a hill would get its circle drawn off-screen while still stealing your FCS lead. When the report expires or that tank is destroyed, the lock is released and the next live report takes over; re-acquiring the same tank does not fire another laser pulse or repeat the notice. If your own fire control computer is destroyed or unpowered, the automatic lock lets go and says so once, because with no ballistic solution in the FCS the circle would only lie to you; it resumes on its own once the FCS is repaired. `SpotTargetLead` and `SpotLeadMax` were removed with the amber layer.

Version 1.10.0 stopped the spot lock from touching the sight and made it stick to the first target: the mod no longer fires the laser or writes the FCS range for a reported target (the range dial stays entirely yours), and once it has committed to a reported vehicle it keeps that vehicle until it is destroyed - newer reports do not steal the lock, and a target that drives behind cover or off-screen simply loses its box until it comes back. Only four things release it: the vehicle being destroyed, a new mission, `F4` switching the mod off, or switching back to `ManualAlt`.

Version 1.11.0 added the off-screen arrow: when a reported vehicle is outside the gunner sight's circle, a green arrow appears along the inside of that circle pointing at it until it enters the sight, at which point the arrow disappears and the usual box takes over. Targets behind you put the arrow on the left or right edge. The arrow only points - no laser, no fire-control writes, no turret movement. `SightCirclePercent` sizes the circle (default 100 = full screen height, lower it if your optic glass is narrower), `SightArrowThickness` sets the arrow width. Version 1.11.1 skips infantry entirely: a reported man gets no box, no lead circle and no arrow, and the next live vehicle report is used instead.

The mod ships no models or textures: every box, line, circle and arrow is drawn on screen, and the only art involved is GHPC's own reticle and optic, loaded by the game at runtime. Nothing is redistributed and no retopology is involved.

MIT，与 Radian Simulations 无关。在 0.1.0-alpha+20261014 上实测过，游戏更新有可能让它失效。
