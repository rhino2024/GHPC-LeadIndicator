using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(LeadIndicator.LeadIndicatorMod), "提前量指示器", "1.7.0", "rhino2024")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace LeadIndicator
{
    // 炮手镜里按 Alt 把准星下的敌方载具锁住：绿框标车体，绿线连到按炮弹飞行时间算出来的提前量小圈，
    // 把准星压在小圈上开火，炮弹飞完这段时间目标正好走进弹着点。锁定期间每帧重算，目标和提前量都跟着走。
    //
    // 1.7.0：按用户要求**去掉自动跟踪**，模组回到纯指示器——只画框、只画圈、只报距离，炮口一律不动，
    //     要不要把准星压到圈上是玩家自己的事。删掉的是整条 AutoTrack 链路：AutoTrack / TrackLeadPoint /
    //     AimAtImpactPoint / TrackMaxAheadDegrees / TrackSmoothingSeconds 五个开关、每帧算命令方向的
    //     UpdateTrackDirection、PlayerInput::Update 的后置补丁，以及补丁里那两下 SetAimVector +
    //     NotifyAimOverride（游戏自己的「Aim Override」通道，会真的转动炮塔）。
    //     保留压制火控提前量（SuppressFcsLead）和自动校正距离（AutoRange）：这两条跟转不转炮无关，
    //     少了它们，画出来的圈就不是「压上去能打中」的那个点。
    //
    // 1.6.1：Alt 是纯粹的开关（用户「按下alt是开，再按一次是关」）。以前按一下是「先解除、再按准星下
    //     那辆车重锁」，所以瞄着车永远关不掉；现在已开着时按一下一律解除，换目标就是关一下、挪准星、开一下。
    //
    // 为什么必须自己算提前量：GHPC 的火控确实会补提前量，但它补的依据不是目标速度，而是
    // FireControlSystem.GetLeadWorldOffset() 里拿 _averageTraverseRate（你转镜头的角速度）× 当前距离
    // × 飞行时间 推出来的“隐含目标速度”。玩家把镜子稳稳压在移动目标上，火控才慢慢攒出这一份提前量；
    // 反过来，玩家要是按我们的圈往前多压一截，火控会把这段额外转动也算成目标跑得更快，再补一份 —— 双倍提前。
    // 所以锁定期间必须把 _leadUpdatesLocked 置真、_lockedTraverseRate 清零，把火控那一路提前量压住，
    // 只留下我们的世界坐标提前量；解除锁定时原样还原。这两个字段是私有的，用 Harmony 的 FieldRef 直写，
    // 不走 FieldInfo 装箱（每帧两次）。
    //
    // 飞行时间取自游戏自己的弹道表：BallisticComputerRepository.GetFlightTime(AmmoType, range)，
    // 比 range / 初速 准（含阻力）。距离用炮口到目标 Centre 的直线距离，并且默认把火控距离也校正到同一个值
    // （AutoRange）—— 瞄准线和火炮超 elevation 说的是同一个距离，圈才真的“压上去就能中”；
    // 没有激光测距仪的车，游戏自己的测距本来就没法用，这一步直接替它把距离写进 FCS。
    //
    // 目标从 SceneUnitsManager.AllUnitsInScene 里按“相机正前方夹角最小 + 排除同阵营/中立 + 视距内”挑，
    // 不靠 Physics 射线：车的碰撞体挂在独立子树里时，命中体反查 GHPC.Unit 不可靠（HitCam 那次踩过）。
    // 车体屏幕矩形用车体在自身车系坐标下的包围盒（由 Collider 累加，剔掉炮管和巨型面片）八个角投影，
    // 锁定时算一次，每帧只跟着车体 transform 转。
    //
    // 画在 OnGUI 里（游戏自己的准星也走这条通道），OnInitializeMelon 里建的 GameObject 会被任务场景销毁。
    public sealed class LeadIndicatorMod : MelonMod
    {
        static MelonPreferences_Category cat;
        static MelonPreferences_Entry<bool> eEnabled, eShowBox, eShowCircle, eShowRange,
            eSuppressLead, eAutoRange, eRequireVisible, eDebugLog;
        static MelonPreferences_Entry<KeyCode> eToggle, eRangeKey;
        static MelonPreferences_Entry<float> eLockAngle, eMaxRange, eCircleRadius, eBoxPadding,
            eMinScreenRadius;

        static readonly FieldInfo FullControlField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.Player.PlayerInput), "_hasFullVehicleControl");
        static readonly FieldInfo SeatField =
            HarmonyLib.AccessTools.Field(typeof(GHPC.Player.PlayerInput), "_controlledPosition");

        static AccessTools.FieldRef<GHPC.Weapons.FireControlSystem, bool> LeadLockRef;
        static AccessTools.FieldRef<GHPC.Weapons.FireControlSystem, Vector2> LeadRateRef;

        static StreamWriter log;
        static string noticeText = "";
        static float noticeTimer;
        static GUIStyle noticeStyle;

        static GHPC.Unit target;
        static GHPC.Unit lockedPlayer;
        static GHPC.Weapons.WeaponSystemInfo lockedWeapon;
        static Renderer[] targetRenderers;
        static Collider[] targetColliders;

        // 绿框量的是车体在“自己车系坐标”下的包围盒：世界轴对齐的盒子在斜着停车时会比车宽出
        // 1.4 倍，而且会把炮管、烟柱一起并进来。锁定时在车体系里算好一次，每帧只跟着车转
        static Transform boxFrame;
        static Vector3 boxCenter;
        static Vector3 boxExtents;
        static bool haveLocalBox;
        static Vector3 smoothVelocity;

        // 调试读数：圈画在屏幕上离中心多少像素、相机光轴离提前点差多少度。
        // 这两个只描述「我们算出来的点在哪」，不参与任何瞄准，去掉自动跟踪后仍然留着排查用。
        static float viewErrorDeg = -1f;
        static float leadPixelOffset = -1f;

        // 火控提前量的原始状态，解除锁定时逐项还原
        static GHPC.Weapons.FireControlSystem leadFcs;
        static bool leadCaptured;
        static bool savedLeadLocked;
        static Vector2 savedLeadRate;

        // OnGUI 用的当帧几何
        static Rect boxRect;
        static bool haveBox;
        static bool haveLead;
        static Vector2 leadScreen;
        static string rangeText = "";
        static int rangeInt = -1;

        static Texture2D whiteTex;
        static Texture2D ringTex;
        static GUIStyle rangeStyle;

        public override void OnInitializeMelon()
        {
            cat = MelonPreferences.CreateCategory("LeadIndicator", "提前量指示器");
            eEnabled = cat.CreateEntry<bool>("Enabled", true,
                "主开关。关掉后按锁定键不再框目标");
            eToggle = cat.CreateEntry<KeyCode>("ToggleKey", KeyCode.F4,
                "任务里开关这个模组的按键");
            eRangeKey = cat.CreateEntry<KeyCode>("RangeKey", KeyCode.LeftAlt,
                "在炮手镜里单按它打开：锁定准星下的敌方载具并测距、画出提前圈；再按一次就是关掉（不管准星下有没有车）。"
                + "想换目标就先按一下关掉、挪准星、再按一下打开。只有真按下了这一个键、期间没碰别的键才算一次，"
                + "Alt+数字选 GO 码、Alt+Tab 切窗口都不会误触发");
            eShowBox = cat.CreateEntry<bool>("ShowBox", true, "在目标车体上画绿色方框");
            eShowCircle = cat.CreateEntry<bool>("ShowLeadCircle", true,
                "从绿框画一条绿线连到提前量小圈；把准星压在小圈上开火");
            eShowRange = cat.CreateEntry<bool>("ShowRange", true, "在绿框下面显示测出的距离");
            eSuppressLead = cat.CreateEntry<bool>("SuppressFcsLead", true,
                "锁定期间压掉火控自己的提前量。GHPC 的 FCS 是按你转镜头的角速度反推提前量的，"
                + "不压掉的话，你把准星往我们画的圈上移，它会在圈上再加一份，变成双倍提前");
            eAutoRange = cat.CreateEntry<bool>("AutoRange", true,
                "锁定期间把火控距离校正到目标真实距离。没有激光测距仪的车靠这个才能直接打中");
            eLockAngle = cat.CreateEntry<float>("LockConeDegrees", 4f,
                "按锁定键时，相机正前方多少度以内算“准星指着它”（角度越大越容易锁到旁边的车）");
            eMaxRange = cat.CreateEntry<float>("MaxRange", 6000f, "超过这个距离（米）的载具不锁定");
            eCircleRadius = cat.CreateEntry<float>("CircleRadius", 14f,
                "提前量小圈的半径（1080p 下的像素，高分屏会按比例放大）");
            eBoxPadding = cat.CreateEntry<float>("BoxPadding", 4f, "绿框比车体包围盒外扩多少像素");
            eRequireVisible = cat.CreateEntry<bool>("RequireLineOfSight", true,
                "只锁视野里真看得见的目标。借用游戏 AI 自己那套判断（GHPC.AI.VisibilityManager.IsPositionVisible："
                + "从炮镜往目标中心打射线，命中地形＝看不见，命中别的车的视准盒＝被挡住），"
                + "所以山后面、树线后面那类「准星方向上恰好有一辆几公里外的车」不会再被锁上；"
                + "顺带每帧复查，目标躲到山背后绿框就消失");
            eMinScreenRadius = cat.CreateEntry<float>("MinScreenRadiusPixels", 12f,
                "目标在屏幕上的尺寸门槛：拿游戏自己的车体可见性点位（Unit.Extremities，车体四角）量「中心到最远点」的"
                + "像素半径，不足这个数就不锁定。远处的车在屏幕上只有几个像素，你肉眼认不出来，模组也不该替你锁；"
                + "炮镜放大倍率越大这个门槛越容易过，所以瞄具拉着打远距离不受影响。填 0 关掉这条");
            eDebugLog = cat.CreateEntry<bool>("DebugLog", false,
                "把锁定/测距/飞行时间/火控距离写进 Bin/Mods/LeadIndicator.debug.log，只在排查问题时打开");

            try
            {
                LeadLockRef = AccessTools.FieldRefAccess<GHPC.Weapons.FireControlSystem, bool>("_leadUpdatesLocked");
                LeadRateRef = AccessTools.FieldRefAccess<GHPC.Weapons.FireControlSystem, Vector2>("_lockedTraverseRate");
            }
            catch (Exception e)
            {
                LeadLockRef = null;
                LeadRateRef = null;
                MelonLogger.Warning("提前量指示器：找不到 FCS 的提前量字段，压制火控提前量失效：" + e.Message);
            }

            if (eDebugLog.Value) OpenLog();
            MelonLogger.Msg("提前量指示器 已加载（炮手镜里按 " + LockKeyName() + " 锁定，"
                + eToggle.Value + " 开关，" + (eEnabled.Value ? "现在是开的" : "现在是关的") + "）");
            Log("LeadIndicator 1.7.0 initialised, enabled=" + eEnabled.Value
                + " lock key=" + LockKeyName() + " toggle=" + eToggle.Value
                + " suppress fcs lead=" + eSuppressLead.Value + " auto range=" + eAutoRange.Value
                + " cone=" + eLockAngle.Value + " max range=" + eMaxRange.Value);
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(eToggle.Value))
            {
                eEnabled.Value = !eEnabled.Value;
                try { cat.SaveToFile(false); } catch (Exception e) { Log("saving threw: " + e.Message); }
                if (!eEnabled.Value) Release("mod switched off");
                ShowNotice(eEnabled.Value
                    ? "提前量指示器 已开启 - 炮手镜里按 " + LockKeyName() + " 锁定目标"
                    : "提前量指示器 已关闭 - 按 " + eToggle.Value + " 恢复");
                Log("feature " + (eEnabled.Value ? "on" : "off"));
            }

            if (noticeTimer > 0f) noticeTimer -= Time.unscaledDeltaTime;

            GHPC.Player.PlayerInput pi;
            if (!eEnabled.Value || !GunnerReady(out pi))
            {
                // 退出炮镜时 Alt 可能还按在半路，结算状态得跟着丢掉，不然回来时会补上一次切换
                altIsDown = false;
                altWasCombined = false;
                if (target != null) Release(eEnabled.Value ? "left the gunner sight" : "mod is off");
                return;
            }

            if (LockKeyPressed())
            {
                // Alt 是开关不是「选目标」：已经开着就一律关掉，不管准星下有没有车；
                // 想换目标就先按一下关掉、挪准星、再按一下打开
                if (target != null)
                {
                    Release("lock key switched it off");
                    ShowNotice("提前量指示器 已关闭 - 再按 " + LockKeyName() + " 锁定准星下的目标");
                }
                else LockTarget(pi);
            }

            if (target == null) return;

            var cam = MainCam();
            string why = null;
            if (cam == null || !StillValid(pi, cam, out why))
            {
                Release(why ?? "target lost");
                return;
            }

            UpdateLock(pi, cam);
        }

        // 游戏自己就把 Alt 当修饰键用（PlayerInput 里 ldstr "LeftAlt" 取按下状态，接着 Alt+1..9 触发
        // GoCodeShortcutPressed），所以锁定不能在 Alt 落下的那一帧切换，否则每次选 GO 码、每次 Alt+Tab
        // 都会顺手把目标锁上或解掉。改成“抬起时结算”：Alt 抬起的这一刻，只有全程没碰过其它键才算单按。
        static bool altIsDown;
        static bool altWasCombined;

        static readonly KeyCode[] KeysThatCancelTap =
        {
            KeyCode.Tab, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.Space, KeyCode.Escape,
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4,
            KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9
        };

        static bool LockKeyPressed()
        {
            KeyCode key = eRangeKey.Value;
            KeyCode twin = OtherAlt(key);

            if (!altIsDown)
            {
                if (!Input.GetKeyDown(key) && (twin == KeyCode.None || !Input.GetKeyDown(twin))) return false;
                altIsDown = true;
                altWasCombined = false;
                return false;
            }

            if (!Input.GetKey(key) && (twin == KeyCode.None || !Input.GetKey(twin)))
            {
                altIsDown = false;
                return !altWasCombined;
            }

            for (int i = 0; i < KeysThatCancelTap.Length; i++)
            {
                if (Input.GetKeyDown(KeysThatCancelTap[i])) altWasCombined = true;
            }
            return false;
        }

        // 锁定键设在某一侧 Alt 上时，另一侧按下去一样算
        static KeyCode OtherAlt(KeyCode key)
        {
            if (key == KeyCode.LeftAlt) return KeyCode.RightAlt;
            if (key == KeyCode.RightAlt) return KeyCode.LeftAlt;
            return KeyCode.None;
        }

        static string LockKeyName()
        {
            KeyCode key = eRangeKey.Value;
            if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt) return "Alt";
            return key.ToString();
        }

        // ---------------------------------------------------------------- target selection

        void LockTarget(GHPC.Player.PlayerInput pi)
        {
            var cam = MainCam();
            var own = pi.CurrentPlayerUnit;
            if (cam == null || own == null) { ShowNotice("还没有进入载具"); return; }

            GHPC.Unit best = null;
            float bestAngle = eLockAngle.Value;
            float bestDist = 0f;
            // 离准星最近那个候选被挡掉的原因，用来告诉玩家为什么“看着有车却锁不上”
            string reject = null;

            List<GHPC.Unit> units = null;
            try
            {
                var mgr = GHPC.World.SceneUnitsManager.Instance;
                if (mgr != null && mgr.AllUnitsInScene != null)
                    units = new List<GHPC.Unit>(mgr.AllUnitsInScene);
            }
            catch (Exception e) { Log("SceneUnitsManager threw: " + e.Message); }
            if (units == null)
            {
                try { units = new List<GHPC.Unit>(UnityEngine.Object.FindObjectsOfType<GHPC.Unit>()); }
                catch (Exception e) { Log("FindObjectsOfType threw: " + e.Message); return; }
            }

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || u == own) continue;
                if (u.Destroyed) continue;
                if (u.Allegiance == own.Allegiance || u.Allegiance == GHPC.Faction.Neutral) continue;

                Vector3 centre;
                try
                {
                    var c = u.Center;
                    if (c == null) continue;
                    centre = c.position;
                }
                catch { continue; }

                Vector3 to = centre - cam.transform.position;
                float d = to.magnitude;
                if (d < 5f || d > eMaxRange.Value) continue;

                float angle = Vector3.Angle(cam.transform.forward, to);
                if (angle >= bestAngle) continue;
                string blocked = RejectReason(cam, own, u, centre);
                if (blocked != null)
                {
                    if (reject == null) reject = blocked;
                    continue;
                }
                bestAngle = angle;
                best = u;
                bestDist = d;
            }

            if (best == null)
            {
                ShowNotice(reject != null
                    ? "准星下的目标" + reject + "（" + eLockAngle.Value.ToString("0.#") + " 度以内没有能锁的）"
                    : "准星下没有敌方载具（" + eLockAngle.Value.ToString("0.#") + " 度以内）");
                Log("lock failed: " + (reject != null ? reject : "nothing within " + eLockAngle.Value + " deg"));
                return;
            }

            target = best;
            lockedPlayer = own;
            try
            {
                var brain = pi.CurrentPlayerCrewBrain;
                var module = brain != null ? brain.WeaponsModule : null;
                lockedWeapon = module != null ? module.ActiveWeapon : null;
            }
            catch { lockedWeapon = null; }

            // 让游戏自己的激光走一遍。没有激光测距仪的车这里什么都不发生，后面 AutoRange 会直接写距离
            try { if (pi.CanRange) pi.Lase(); } catch (Exception e) { Log("Lase threw: " + e.Message); }

            CacheShape(best);
            string name = SafeName(best);
            ShowNotice("已锁定 " + name + " · " + Mathf.RoundToInt(bestDist) + " m");
            Log("locked " + name + " (" + best.Type + ") at " + bestDist.ToString("0.0")
                + " m, angle " + bestAngle.ToString("0.00") + " deg, weapon="
                + (lockedWeapon != null ? lockedWeapon.Role + "/" + lockedWeapon.Name : "none"));
        }

        // 只按角度挑最近的一辆会锁到山后面、树线后面那些“肉眼根本不在视野里”的车（2026-10-24 用户实测）：
        // 几公里外的目标在屏幕上只有几个像素，四度锥照样把它算进“准星指着它”。这两道门槛把“看得见”补回来。
        static string RejectReason(Camera cam, GHPC.Unit own, GHPC.Unit u, Vector3 centre)
        {
            if (eRequireVisible.Value && !GameCanSee(own, cam.transform.position, u, centre))
                return "被地形或别的车挡住、视野里看不见";

            float min = eMinScreenRadius.Value;
            if (min > 0f)
            {
                float radius = ScreenRadius(cam, u, centre);
                if (radius >= 0f && radius < min)
                    return "离得太远，在屏幕上只有 " + (radius * 2f).ToString("0") + " 像素";
            }
            return null;
        }

        // GHPC.AI.VisibilityManager.IsPositionVisible 是游戏 AI 判断“这个位置我看不看得见”的那条静态射线。
        // IL（all_ghpc.txt:207435）逐条核实：sqrMagnitude 超 sqrMaxDistance 直接 false；self 为 null 直接 false；
        // 从 origin+dir*0.1 用 ConstantsAndInfoManager.VisibilityTestLayerMask 打一条射线，命中 Terrain false，
        // 命中「既不是 Vision Hitbox(28) 也没打 BoundingBox 标签」的东西 false，命中某辆非目标车的视准盒 false，
        // 命中目标自己或根本没打中才继续，后面还补两针垂直射线查 Forest。参数照抄游戏调用最多的一组
        // （grassBlocks=true, terrainCheckOnly=false, skipTreesAndSmoke=false, treatTreesAsOpaque=false）。
        // 炮镜相机在自己车体的视准盒里面，Unity 默认 queriesHitBackfaces=false，从内部出发的射线不会打到自己，
        // 所以这条射线天然不会被玩家自己的车挡掉（游戏 AI 也是这么用的）。
        static bool GameCanSee(GHPC.Unit own, Vector3 from, GHPC.Unit wanted, Vector3 to)
        {
            if (own == null) return true;

            float dist = Vector3.Distance(from, to);
            float max = dist + 25f;
            float penalty;
            GHPC.Unit struck;
            try
            {
                bool visible = GHPC.AI.VisibilityManager.IsPositionVisible(own, from, wanted, to,
                    max, max * max, out penalty, out struck, true, false, false, false);
                if (!visible)
                {
                    Log("LOS rejected " + SafeName(wanted) + " at " + dist.ToString("0")
                        + " m, blocker=" + (struck != null ? SafeName(struck) : "terrain or other collider"));
                }
                return visible;
            }
            catch (Exception e)
            {
                // 判不了就别拦玩家：宁可多锁一辆，也不要出现“视野里明明有车却锁不上”
                LogOnce("visibility check threw: " + e.Message);
                return true;
            }
        }

        // 尺寸用游戏自己的车体可见性点位量（Unit.Extremities = 私有 VisibilityLocations，车体四角那一圈 transform），
        // 比我们自己的碰撞盒便宜，而且它本来就是“这辆车能被看见的范围”
        static float ScreenRadius(Camera cam, GHPC.Unit u, Vector3 centre)
        {
            Vector3 origin = cam.WorldToScreenPoint(centre);
            if (origin.z <= 0.01f) return -1f;

            List<Transform> points = null;
            try { points = u.Extremities; } catch { points = null; }
            if (points == null || points.Count == 0) return -1f;

            float best = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                var t = points[i];
                if (t == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(t.position);
                if (sp.z <= 0.01f) continue;
                float dx = sp.x - origin.x, dy = sp.y - origin.y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > best) best = d;
            }
            return best;
        }

        static bool GunnerReady(out GHPC.Player.PlayerInput pi)
        {
            pi = GHPC.Player.PlayerInput.Instance;
            if (pi == null) return false;
            if (GHPC.AarController.InAar) return false;
            if (!pi.PlayerCanControlGunnerNow) return false;
            if (pi.UsingBinoculars) return false;
            if (pi.IsExteriorMode) return false;
            if (FullControlField != null && SeatField != null)
            {
                try
                {
                    bool full = (bool)FullControlField.GetValue(pi);
                    int seat = (int)SeatField.GetValue(pi);
                    if (!full && seat != 2) return false;
                }
                catch { return false; }
            }
            return MainCam() != null;
        }

        static bool StillValid(GHPC.Player.PlayerInput pi, Camera cam, out string why)
        {
            why = null;
            if (target == null) { why = "target gone"; return false; }
            if (lockedPlayer != null && pi.CurrentPlayerUnit != lockedPlayer) { why = "player switched unit"; return false; }

            GameObject go;
            try { go = target.gameObject; } catch { why = "target gone"; return false; }
            if (go == null || !go.activeInHierarchy) { why = "target inactive"; return false; }

            bool destroyed;
            try { destroyed = target.Destroyed; } catch { destroyed = false; }
            if (destroyed) { why = "target destroyed"; return false; }

            Vector3 centre;
            try
            {
                var c = target.Center;
                if (c == null) { why = "target centre lost"; return false; }
                centre = c.position;
            }
            catch { why = "target centre lost"; return false; }

            Vector3 to = centre - cam.transform.position;
            if (to.magnitude > eMaxRange.Value * 1.25f) { why = "target out of range"; return false; }
            if (Vector3.Angle(cam.transform.forward, to) > 35f) { why = "target left the view"; return false; }
            // 目标躲到山背后就该脱锁，绿框跟着消失，和“只锁视野里的”是同一条规矩
            if (eRequireVisible.Value && !GameCanSee(pi.CurrentPlayerUnit, cam.transform.position, target, centre))
            { why = "target is behind cover"; return false; }
            return true;
        }

        // ---------------------------------------------------------------- per-frame aim

        void UpdateLock(GHPC.Player.PlayerInput pi, Camera cam)
        {
            GHPC.Weapons.WeaponSystemInfo info = null;
            try
            {
                var brain = pi.CurrentPlayerCrewBrain;
                var module = brain != null ? brain.WeaponsModule : null;
                info = module != null ? module.ActiveWeapon : null;
            }
            catch { info = null; }

            if (info == null) { Release("no active weapon"); return; }
            if (lockedWeapon != null && info != lockedWeapon) { Release("active weapon changed"); return; }

            var fcs = info.FCS;
            var weapon = info.Weapon;

            Vector3 centre;
            try
            {
                var c = target.Center;
                if (c == null) { Release("target centre lost"); return; }
                centre = c.position;
            }
            catch { Release("target centre lost"); return; }

            Vector3 origin = cam.transform.position;
            if (weapon != null && weapon.MuzzleIdentity != null) origin = weapon.MuzzleIdentity.position;

            float distance = Vector3.Distance(origin, centre);
            if (distance < 5f) { Release("target too close"); return; }

            ApplyRange(fcs, distance);
            if (eSuppressLead.Value) SuppressLead(fcs);
            else RestoreLead();

            AmmoType ammo = null;
            try { ammo = weapon != null ? weapon.CurrentAmmoType : null; } catch { ammo = null; }

            Vector3 raw = Vector3.zero;
            try { raw = target.Velocity; } catch { raw = Vector3.zero; }
            // 车体速度是刚体逐帧读来的，直接用会让小圈一跳一跳；抹平到 ~0.2 秒的时间常数，
            // 目标真的转向时提前点照样跟得上
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.2f);
            smoothVelocity = Vector3.Lerp(smoothVelocity, raw, blend);
            Vector3 velocity = smoothVelocity;

            // 先按当前距离估一次飞行时间，再用提前点的距离迭代一次，够收敛了
            float flight = FlightTime(ammo, distance);
            Vector3 lead = centre + velocity * flight;
            flight = FlightTime(ammo, Vector3.Distance(origin, lead));
            lead = centre + velocity * flight;

            haveBox = ProjectBox(cam, out boxRect);
            haveLead = haveBox && TryProject(cam, lead, out leadScreen);
            // 圈离屏幕中心多少个像素：这是「按现在这套算法，屏幕上圈和准星差多远」的直接读数
            leadPixelOffset = haveLead
                ? Vector2.Distance(leadScreen, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f))
                : -1f;
            viewErrorDeg = -1f;
            try { viewErrorDeg = Vector3.Angle(cam.transform.forward, lead - cam.transform.position); } catch { }

            int metres = Mathf.RoundToInt(distance);
            if (metres != rangeInt)
            {
                rangeInt = metres;
                rangeText = metres + " m";
            }

            LogThrottled(distance, flight, velocity.magnitude, fcs);
        }

        void ApplyRange(GHPC.Weapons.FireControlSystem fcs, float distance)
        {
            if (!eAutoRange.Value || fcs == null) return;
            try
            {
                float current = fcs.TargetRange;
                float tolerance = Mathf.Max(50f, distance * 0.08f);
                if (current > 1f && Mathf.Abs(current - distance) <= tolerance) return;
                fcs.TargetRange = distance;
                Log("fcs range -> " + distance.ToString("0") + " m (was " + current.ToString("0") + ")");
            }
            catch (Exception e) { Log("setting fcs range threw: " + e.Message); }
        }

        static float FlightTime(AmmoType ammo, float range)
        {
            if (range < 1f) range = 1f;
            if (ammo != null)
            {
                try
                {
                    var repo = GHPC.Weapons.BallisticComputerRepository.Instance;
                    if (repo != null)
                    {
                        float t = repo.GetFlightTime(ammo, range);
                        if (t > 0.001f && t < 60f) return t;
                    }
                }
                catch (Exception e) { LogOnce("ballistic table threw: " + e.Message); }

                float mv = 0f;
                try { mv = ammo.MuzzleVelocity; } catch { mv = 0f; }
                if (mv > 1f) return range / mv;
            }
            return range / 1000f;
        }

        // ---------------------------------------------------------------- fcs lead suppression

        void SuppressLead(GHPC.Weapons.FireControlSystem fcs)
        {
            if (fcs == null || LeadLockRef == null || LeadRateRef == null) { RestoreLead(); return; }

            if (!leadCaptured || leadFcs != fcs)
            {
                RestoreLead();
                try
                {
                    savedLeadLocked = LeadLockRef(fcs);
                    savedLeadRate = LeadRateRef(fcs);
                    leadFcs = fcs;
                    leadCaptured = true;
                    Log("captured fcs lead state: locked=" + savedLeadLocked + " rate=" + savedLeadRate);
                }
                catch (Exception e) { Log("capturing fcs lead state threw: " + e.Message); return; }
            }

            try
            {
                ref bool locked = ref LeadLockRef(fcs);
                locked = true;
                ref Vector2 rate = ref LeadRateRef(fcs);
                rate = Vector2.zero;
            }
            catch (Exception e) { LogOnce("suppressing fcs lead threw: " + e.Message); }
        }

        static void RestoreLead()
        {
            if (!leadCaptured) return;
            var fcs = leadFcs;
            leadCaptured = false;
            leadFcs = null;
            if (fcs == null || LeadLockRef == null || LeadRateRef == null) return;
            try
            {
                ref bool locked = ref LeadLockRef(fcs);
                locked = savedLeadLocked;
                ref Vector2 rate = ref LeadRateRef(fcs);
                rate = savedLeadRate;
                Log("restored fcs lead state: locked=" + savedLeadLocked + " rate=" + savedLeadRate);
            }
            catch (Exception e) { Log("restoring fcs lead state threw: " + e.Message); }
        }

        void Release(string why)
        {
            if (target != null) Log("release: " + why);
            RestoreLead();
            target = null;
            lockedPlayer = null;
            lockedWeapon = null;
            targetRenderers = null;
            targetColliders = null;
            boxFrame = null;
            haveLocalBox = false;
            smoothVelocity = Vector3.zero;
            viewErrorDeg = -1f;
            leadPixelOffset = -1f;
            haveBox = false;
            haveLead = false;
            rangeInt = -1;
            rangeText = "";
        }

        // ---------------------------------------------------------------- screen geometry

        static bool ProjectBox(Camera cam, out Rect box)
        {
            box = new Rect();
            var frame = boxFrame;
            if (cam == null || target == null || !haveLocalBox || frame == null) return false;

            Vector3 lo = boxCenter - boxExtents, hi = boxCenter + boxExtents;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            bool any = false;

            for (int i = 0; i < 8; i++)
            {
                Vector3 local = new Vector3(
                    (i & 1) == 0 ? lo.x : hi.x,
                    (i & 2) == 0 ? lo.y : hi.y,
                    (i & 4) == 0 ? lo.z : hi.z);
                Vector2 gui;
                if (!TryProject(cam, frame.TransformPoint(local), out gui)) continue;
                any = true;
                if (gui.x < minX) minX = gui.x;
                if (gui.x > maxX) maxX = gui.x;
                if (gui.y < minY) minY = gui.y;
                if (gui.y > maxY) maxY = gui.y;
            }

            if (!any) return false;
            float pad = Mathf.Max(0f, eBoxPadding.Value);
            float w = maxX - minX, h = maxY - minY;
            if (w < 2f) { minX -= 1f; w = 2f; }
            if (h < 2f) { minY -= 1f; h = 2f; }
            box = new Rect(minX - pad, minY - pad, w + pad * 2f, h + pad * 2f);
            return true;
        }

        const float MaxPartDiagonal = 18f;
        const float MaxPartOffset = 12f;

        // 量框在车体系里做。Collider 才是这发弹真正会撞上的形状，Renderer 只在没有可用 Collider
        // 时兜底：起火车体挂的烟柱火舌、mod 车的巨型附加装甲面片都能把并集撑到车的好几倍。
        static void CacheShape(GHPC.Unit unit)
        {
            haveLocalBox = false;
            boxFrame = null;
            targetRenderers = null;
            targetColliders = null;
            try
            {
                targetRenderers = CollectRenderers(unit, false);
                targetColliders = CollectColliders(unit, false);

                var frame = unit.transform;
                Vector3 anchor = Vector3.zero;
                try
                {
                    var c = unit.Center;
                    if (c != null) anchor = frame.InverseTransformPoint(c.position);
                }
                catch { anchor = Vector3.zero; }

                bool measured = TryLocalBox(targetColliders, frame, anchor)
                    || TryLocalBox(targetRenderers, frame, anchor);

                string pass = "active";
                if (!measured)
                {
                    // 烧起来的残骸会把部件 SetActive(false)，上面那一遍只收得到还活着的部件，
                    // 于是整辆车会退到合成尺寸。再量一遍连未激活的部件一起
                    targetColliders = CollectColliders(unit, true);
                    targetRenderers = CollectRenderers(unit, true);
                    measured = TryLocalBox(targetColliders, frame, anchor)
                        || TryLocalBox(targetRenderers, frame, anchor);
                    pass = "all";
                }

                if (measured)
                {
                    boxFrame = frame;
                    haveLocalBox = true;
                }
                else if (frame != null)
                {
                    // 一块都没量到：给一辆中型载具的常见尺寸，至少提前量小圈还有落点可画
                    boxCenter = anchor;
                    boxExtents = new Vector3(3.3f, 1.4f, 3.3f);
                    boxFrame = frame;
                    haveLocalBox = true;
                }

                Log("shape (" + pass + "): " + targetColliders.Length + " colliders / "
                    + targetRenderers.Length + " renderers -> hull " + (boxExtents * 2f).ToString("0.0")
                    + " | largest part " + Largest(targetColliders));
            }
            catch (Exception e) { Log("caching shape threw: " + e.Message); }
        }

        static Collider[] CollectColliders(GHPC.Unit unit, bool includeInactive)
        {
            var cols = unit.GetComponentsInChildren<Collider>(includeInactive);
            var hull = new List<Collider>(cols.Length);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null && !cols[i].isTrigger) hull.Add(cols[i]);
            return hull.ToArray();
        }

        static Renderer[] CollectRenderers(GHPC.Unit unit, bool includeInactive)
        {
            var found = unit.GetComponentsInChildren<Renderer>(includeInactive);
            var keep = new List<Renderer>(found.Length);
            for (int i = 0; i < found.Length; i++)
            {
                var r = found[i];
                if (r is MeshRenderer || r is SkinnedMeshRenderer) keep.Add(r);
            }
            return keep.ToArray();
        }

        static bool TryLocalBox(Component[] parts, Transform frame, Vector3 anchorLocal)
        {
            if (parts == null || frame == null) return false;
            bool any = false;
            Vector3 lo = Vector3.zero, hi = Vector3.zero;

            for (int i = 0; i < parts.Length; i++)
            {
                Bounds hullLocal;
                if (!TryPartGeometry(parts[i], frame, out hullLocal)) continue;

                Vector3 c = hullLocal.center;
                Vector3 e = hullLocal.extents;
                float diagonal = e.magnitude * 2f;
                // 真车上没有任何一块单独几何撑得到 18 米对角线，也没有任何东西会离车身中心 12 米以上
                if (diagonal < 0.05f || diagonal > MaxPartDiagonal) continue;
                if ((c - anchorLocal).sqrMagnitude > MaxPartOffset * MaxPartOffset) continue;

                // 炮管是挂在炮塔上的一根细长块，能把框往前多拽出两三米，不算车体
                float longest = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
                float shortest = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
                float middle = e.x + e.y + e.z - longest - shortest;
                if (longest > 1.25f && middle < 0.25f) continue;

                if (!any) { lo = c - e; hi = c + e; any = true; }
                else
                {
                    lo = Vector3.Min(lo, c - e);
                    hi = Vector3.Max(hi, c + e);
                }
            }

            if (!any) return false;
            boxCenter = (lo + hi) * 0.5f;
            boxExtents = (hi - lo) * 0.5f;
            return true;
        }

        // 部件按它自己的局部几何 + 它自己的变换搬进车体系。以前搬的是 Collider.bounds —— 那是世界轴
        // 对齐包围盒，车头一斜，每块部件的盒子先被放大到 1.4 倍，再并起来就是「高度看着对、宽度离谱」
        // 的框，而且这个误差跟车怎么停有关，没法靠调常数压掉。
        static bool TryPartGeometry(Component part, Transform frame, out Bounds hullLocal)
        {
            hullLocal = new Bounds();
            if (part == null || frame == null || part.transform == null) return false;

            Bounds local;
            if (!TryLocalGeometry(part, out local)) return false;

            Matrix4x4 toHull = frame.worldToLocalMatrix * part.transform.localToWorldMatrix;
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Vector3 mn = local.min, mx = local.max;

            for (int i = 0; i < 8; i++)
            {
                Vector3 p = toHull.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? mn.x : mx.x,
                    (i & 2) == 0 ? mn.y : mx.y,
                    (i & 4) == 0 ? mn.z : mx.z));
                if (float.IsNaN(p.x) || float.IsInfinity(p.x)) return false;
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }

            Vector3 center = (lo + hi) * 0.5f;
            Vector3 extents = (hi - lo) * 0.5f;
            if (extents.magnitude <= 0.01f) return false;
            hullLocal = new Bounds(center, extents * 2f);
            return true;
        }

        // Collider.bounds 是唯一的世界轴包围盒来源，这里要的是部件自己那份没被旋转撑大的尺寸：
        // 各形状都用自己的参数，MeshCollider 取网格的本地包围盒，Renderer 直接问 localBounds。
        // WheelCollider 故意不量：它的 bounds 是一个和车轮外形无关的占位盒，而轮子本来也不该撑框
        static bool TryLocalGeometry(Component part, out Bounds local)
        {
            local = new Bounds();

            var box = part as BoxCollider;
            if (box != null)
            {
                if (box.size.x <= 0f || box.size.y <= 0f || box.size.z <= 0f) return false;
                local = new Bounds(box.center, box.size);
                return true;
            }

            var sphere = part as SphereCollider;
            if (sphere != null)
            {
                if (sphere.radius <= 0f) return false;
                local = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                return true;
            }

            var capsule = part as CapsuleCollider;
            if (capsule != null)
            {
                float diameter = capsule.radius * 2f;
                if (diameter <= 0f) return false;
                Vector3 size = Vector3.one * diameter;
                if (capsule.height > diameter)
                {
                    if (capsule.direction == 1) size.y = capsule.height;
                    else if (capsule.direction == 2) size.z = capsule.height;
                    else size.x = capsule.height;
                }
                local = new Bounds(capsule.center, size);
                return true;
            }

            var meshCollider = part as MeshCollider;
            if (meshCollider != null)
            {
                var mesh = meshCollider.sharedMesh;
                if (mesh == null) return false;
                local = mesh.bounds;
                return true;
            }

            var renderer = part as Renderer;
            if (renderer != null)
            {
                local = renderer.localBounds;
                return true;
            }

            return false;
        }

        static string Largest(Component[] parts)
        {
            if (parts == null) return "none";
            float best = -1f;
            string name = "none";
            for (int i = 0; i < parts.Length; i++)
            {
                Bounds b;
                if (!TryBounds(parts[i], out b)) continue;
                float size = b.size.magnitude;
                if (size <= best) continue;
                best = size;
                name = parts[i].gameObject.name + " " + size.ToString("0.0") + "m";
            }
            return name;
        }

        static bool TryBounds(Component part, out Bounds bounds)
        {
            bounds = new Bounds();
            if (part == null) return false;
            var col = part as Collider;
            if (col != null) { bounds = col.bounds; return true; }
            var rend = part as Renderer;
            if (rend != null) { bounds = rend.bounds; return true; }
            return false;
        }

        static bool TryProject(Camera cam, Vector3 world, out Vector2 gui)
        {
            gui = Vector2.zero;
            if (cam == null) return false;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.01f) return false;
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        static Camera MainCam()
        {
            try
            {
                var cam = GHPC.Camera.CameraManager.MainCam;
                if (cam != null && cam.isActiveAndEnabled) return cam;
            }
            catch { }
            return Camera.main;
        }

        static string SafeName(GHPC.Unit unit)
        {
            try
            {
                var n = unit.FriendlyName;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            return unit.name;
        }

        // ---------------------------------------------------------------- drawing

        public override void OnGUI()
        {
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;

            DrawNotice();

            if (target != null && haveBox && eEnabled.Value && !GHPC.AarController.InAar)
            {
                EnsureTextures();
                float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
                var green = new Color(0.25f, 1f, 0.35f, 0.95f);
                float thickness = Mathf.Max(1f, Mathf.Round(2f * scale));

                if (Event.current == null || Event.current.type == EventType.Repaint)
                {
                    if (eShowBox.Value) DrawOutline(boxRect, thickness, green);
                    if (eShowCircle.Value && haveLead)
                    {
                        DrawLine(boxRect.center, leadScreen, thickness,
                            new Color(green.r, green.g, green.b, 0.55f));
                        DrawRing(leadScreen, Mathf.Max(5f, eCircleRadius.Value * scale), green);
                    }
                }

                if (eShowRange.Value && eShowBox.Value && !string.IsNullOrEmpty(rangeText))
                {
                    if (rangeStyle == null)
                    {
                        rangeStyle = new GUIStyle(GUI.skin.label);
                        rangeStyle.fontSize = Mathf.RoundToInt(14f * scale);
                        rangeStyle.alignment = TextAnchor.UpperLeft;
                        rangeStyle.normal.textColor = green;
                    }
                    GUI.Label(new Rect(boxRect.x, boxRect.yMax + 2f * scale, 220f, 22f * scale),
                        rangeText, rangeStyle);
                }
            }

            GUI.matrix = matrix;
        }

        static void EnsureTextures()
        {
            if (whiteTex == null)
            {
                whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                whiteTex.SetPixel(0, 0, Color.white);
                whiteTex.Apply(false);
                whiteTex.hideFlags = HideFlags.HideAndDontSave;
            }
            if (ringTex == null)
            {
                ringTex = CreateRingTexture(56, 4f, new Color(0.25f, 1f, 0.35f, 1f));
                ringTex.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        // 抗锯齿圆环，形状跟 SlewReticle 那个圈一致，只是固定绿色
        static Texture2D CreateRingTexture(int size, float widthPx, Color color)
        {
            size = Mathf.Max(12, size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float r = size * 0.5f;
            float outer = r - 1f;
            float inner = outer - Mathf.Max(1f, widthPx);
            float aa = 1.1f;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((d - inner) / aa + 0.5f) * Mathf.Clamp01((outer - d) / aa + 0.5f);
                    var c = color;
                    c.a *= Mathf.Clamp01(a);
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }

        static void DrawOutline(Rect r, float thickness, Color color)
        {
            DrawLine(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), thickness, color);
            DrawLine(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), thickness, color);
            DrawLine(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), thickness, color);
            DrawLine(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), thickness, color);
        }

        static void DrawRing(Vector2 centre, float radius, Color color)
        {
            if (ringTex == null) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), ringTex);
            GUI.color = old;
        }

        static void DrawLine(Vector2 a, Vector2 b, float thickness, Color color)
        {
            if (whiteTex == null) return;
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;

            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var matrix = GUI.matrix;
            var old = GUI.color;

            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), whiteTex);

            GUI.matrix = matrix;
            GUI.color = old;
        }

        static void ShowNotice(string text)
        {
            noticeText = text;
            noticeTimer = 2.5f;
        }

        static void DrawNotice()
        {
            if (noticeTimer <= 0f || string.IsNullOrEmpty(noticeText)) return;
            if (noticeStyle == null)
            {
                noticeStyle = new GUIStyle(GUI.skin.label);
                noticeStyle.fontSize = 16;
                noticeStyle.alignment = TextAnchor.MiddleCenter;
            }

            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(noticeTimer * 1.5f));
            GUI.Label(new Rect(0f, Screen.height * 0.07f, Screen.width, 28f), noticeText, noticeStyle);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- lifecycle / logging

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Release("scene loaded: " + sceneName);
            targetRenderers = null;
            targetColliders = null;
            boxFrame = null;
            haveLocalBox = false;
            haveBox = false;
            haveLead = false;
            rangeInt = -1;
            rangeText = "";
            Log("scene loaded: " + sceneName);
        }

        public override void OnApplicationQuit()
        {
            Release("application quits");
            if (log != null) { try { log.Dispose(); } catch { } log = null; }
        }

        void LogThrottled(float distance, float flight, float speed, GHPC.Weapons.FireControlSystem fcs)
        {
            if (!eDebugLog.Value) return;
            logTimer -= Time.unscaledDeltaTime;
            if (logTimer > 0f) return;
            logTimer = 2f;

            string range = "?";
            try { if (fcs != null) range = fcs.CurrentRange.ToString("0"); } catch { }
            string stab = "?";
            try { if (fcs != null) stab = fcs.StabsActive ? "on" : "off"; } catch { }
            Log("target " + SafeName(target) + " dist=" + distance.ToString("0.0")
                + " m fcs range=" + range + " flight=" + flight.ToString("0.00")
                + " s target speed=" + speed.ToString("0.0") + " m/s lead offset="
                + (Mathf.Abs(speed) > 0.05f ? (speed * flight).ToString("0.0") + " m" : "0")
                + (haveLead ? " circle on screen" : " circle off screen")
                // view＝相机光轴离提前点差多少（0 就说明圈画在屏幕正中），px＝圈离屏幕中心多少像素：
                // 这两个只说明我们画的点在哪，模组已经不碰瞄准了
                + " deg, view=" + Deg(viewErrorDeg)
                + " px=" + (leadPixelOffset < 0f ? "-" : leadPixelOffset.ToString("0"))
                + ", stab=" + stab);
        }

        // 没量到（-1）时不要印成 -1.00，看着像误差
        static string Deg(float degrees)
        {
            return degrees < 0f ? "-" : degrees.ToString("0.00");
        }

        float logTimer;

        static string logOnceKey = "";

        static void LogOnce(string message)
        {
            if (logOnceKey == message) return;
            logOnceKey = message;
            Log(message);
        }

        static void OpenLog()
        {
            if (log != null) return;
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods");
                Directory.CreateDirectory(dir);
                log = new StreamWriter(Path.Combine(dir, "LeadIndicator.debug.log"), true, new UTF8Encoding(false))
                { AutoFlush = true };
            }
            catch { log = null; }
        }

        static void Log(string message)
        {
            if (!eDebugLog.Value) return;
            if (log == null) OpenLog();
            if (log == null) return;
            try { log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message); }
            catch { }
        }
    }
}
