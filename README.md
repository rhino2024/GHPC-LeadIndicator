# 提前量指示器

Gunner, HEAT, PC! 的 MelonLoader 模组。炮手镜里按一下 `Alt` 锁住准星下的敌方载具，画绿框、拉一条线到提前量小圈，再报出距离。把准星压到圈上开火，炮弹飞完这段路，目标正好走进弹着点。队友用无线电报过的敌人，不用你锁，也会自动给上橙黄色的提前框。

## 你会看到什么

锁定之后，目标车体上有一个绿框，框下是距离，框和前面的小圆圈之间连一条绿线——那个小圆圈就是提前量：按游戏自己弹道表里的飞行时间算出来的落点。目标在动，圈也跟着走，每帧重算。

锁定期间会压住火控自己那份提前量：GHPC 的 FCS 是按你转镜头的角速度反推提前量的，不压掉的话，你把准星往圈上移，它会在圈上再加一份，变成双倍提前。解除锁定时原样还原。另外可以打开 `AutoRange`，锁定期间把火控距离校正到目标真实距离（没有激光测距仪的车靠这一条才能直接打中），默认关闭。

**圈跟着你炮镜上的标尺走。** 标尺（准星旁显示的 400/800…）就是火控距离，游戏按它把炮身抬起，让弹道在那个距离穿过光轴。所以拿 400 m 标尺打 1000 m 的车，圈会抬到那个标尺下真正该瞄的位置，而不是一律按真实距离画一条水平提前量。标尺等于真实距离时（激光测距过、或 `AutoRange` 改好了距离）偏移为零，圈落回车上，和以前完全一样。

**模组不会动你的炮口。** 1.7.0 把之前的自动跟踪整条去掉了：锁上之后炮塔不转、准星不跑，只画框、只画圈、只报距离；要不要往圈上压是你的操作。`Alt` 是纯粹的开关，开着的时候再按一下一律解除（不管准星下有没有车）。换目标就是按一下关、挪准星、再按一下开。

只锁视野里真看得见的目标：借用游戏 AI 自己的视线判断，山后面、树线后面那类「准星方向上恰好有一辆几公里外的车」不会被锁上；锁着的时候目标躲进山背后，框也会消失。屏幕上一共只有几个像素的远车同样不锁——肉眼看不清的车，模组也不该替你锁。车体被挡住一半、只露出上半截时照样能锁：中心那条视线被挡之后，模组会再去探车体四角，任意一点能看见就算看得见。

**队友报过的车，自动给框。** 车长或队友用无线电报点的那些敌方载具——就是小地图上那几个报点标记——模组会照样画上提前框和提前圈，橙黄色，和你按 `Alt` 自己锁的绿框分开看。数据直接读游戏自己的报点网络，队友没报就没有框，谁都没报点时屏幕上一个都不会多；一条上报 90 秒后过期，框会先变淡再消失。同时最多三辆（`SpotLeadMax`），报得比这个多就留最近收到的那几个。这一层只画不碰炮，准星压不压还是你的事。

**本车火控坏了，这一层整个不给用。** 火控计算机被打掉或者没电，游戏就没有弹道解，落点、飞行时间、抬落全是猜的——这时候画出来的圈只会骗人。所以模组会立刻把报点框整层收掉，并在屏幕上提示一句，修好之后自动恢复。

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
| SpotTargetLead | true | 队友/车长无线电报过的敌人自动画橙黄提前框（和小地图报点同源）；火控损坏时这一层不可用 |
| SpotLeadMax | 3 | 同时最多给几辆报点目标画框（1 到 5），报多了留最近收到的 |
| DebugLog | false | 出问题再开，会写 `Bin\Mods\LeadIndicator.debug.log` |

## 先说清楚

锁定期间会临时改写火控的两个内部状态（提前量 / 距离），解除时逐项还原；这是让圈准的必要条件，不是替你瞄准。炮口方向、炮塔转速、游戏 AI 全都不碰。

车体绿框量的是车体自己的包围盒，不算炮管和烟柱，车斜着停也不会宽出一圈。

## 想自己编译

不用 .NET SDK，也不用 Unity 编辑器，一个 dll 而已，用任何 Roslyn `csc` 对着游戏自带的托管程序集编就行：

```bash
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash src/build.sh deploy
```

`deploy` 会把编出来的 dll 覆盖到 `<游戏目录>\Bin\Mods\`，所以先关游戏。不加 `deploy` 就只编到 `src/bin/`。

## In English

Shows where to aim at a moving target instead of moving your gun for you. Tap `Alt` in the gunner sight to lock the enemy vehicle under the reticle: a green box appears around its hull, a line runs to a small circle marking the lead point computed from the game's own ballistic flight-time table, and the measured range is printed under the box. While locked, the mod suppresses the fire control's own lead (GHPC's FCS infers lead from your turret traverse rate, which would otherwise double it), restores it on unlock, and can optionally correct the FCS range to the true range (`AutoRange`, off by default). It only locks targets that are actually visible to the game's AI line-of-sight check and big enough on screen to be recognised; if the centre line is blocked, the hull corners are probed, so a tank showing only its upper half still locks. The lead circle follows the sight's range dial: with the dial at 400 m and a target at 1000 m, the circle rises to the point you would actually hold for that setting, and it falls back to the plain horizontal lead when the dial matches the true range (or `RespectSightRange` is off). Version 1.7.0 removed the previous auto-tracking chain entirely: the turret is never driven, the mod only draws. `Alt` is a plain on/off toggle, `F4` toggles the whole mod.

Version 1.8.0 adds a second, automatic layer: enemies that your commander or squadmates have reported over the radio — the same marks the minimap uses — get an amber box and lead circle without you locking anything, up to three at a time (`SpotLeadMax`). Reports expire after 90 seconds, so the marks fade out first. If your own fire control computer is destroyed or unpowered, that layer is withdrawn completely and a notice appears, because with no ballistic solution in the FCS the circle would only lie to you; it comes back on its own once the FCS is repaired. Like everything else here, it draws and never touches your gun.

MIT，与 Radian Simulations 无关。在 0.1.0-alpha+20261014 上实测过，游戏更新有可能让它失效。
