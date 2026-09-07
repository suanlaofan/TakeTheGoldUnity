using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Wukong.StacklineClassic
{
    [DisallowMultipleComponent]
    public sealed class StacklineClassicController : MonoBehaviour
    {
        public enum GameState
        {
            Menu,
            Ready,
            Playing,
            Resolving,
            Failing,
            Revive,
            Rescue,
            GameOver,
        }

        private const float BlockHeight = 0.52f;
        // The renderer is a rounded/tapered ingot while collision uses a stable box.  Leave a
        // hairline clearance between successive boxes so neither the visual bevels nor the
        // colliders can read as interpenetrating when the stack gets tall.
        private const float StackContactClearance = 0.008f;
        private const float InitialWidth = 3.1f;
        private const float InitialDepth = 3.1f;
        private const float MinimumExtent = 0.075f;
        private const string BestScoreKey = "Wukong.StacklineClassic.Best";
        private const string GemsKey = "Wukong.StacklineClassic.Gems";
        private const string LivesKey = "Wukong.StacklineClassic.Lives";

        private static readonly string[] ThemeNames =
        {
            "BULLION", "ANTIQUE", "ROYAL", "SUNLIT", "ROSE GOLD", "WHITE GOLD",
        };

        private static readonly int[] ThemeCosts = { 0, 0, 3, 5, 8, 12 };

        private static readonly int[] ChallengeGoals = { 10, 25, 50 };

        [Header("Placement")]
        [SerializeField] private Transform arenaAnchor;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private StacklineHud hud;
        [SerializeField] private StacklineTapTarget tapTarget;
        [SerializeField] private GameObject goldBarPrefab;
        [SerializeField, Tooltip("The PICO VR scene gives the HMD camera pose to the XR runtime. Desktop free-view must stay disabled.")]
        private bool xrMode;
        [SerializeField, Min(0.4f)] private float baseSpeed = 2.5f;
        [SerializeField, Min(1f)] private float movementRange = 3.75f;
        [SerializeField, Range(0.01f, 0.3f)] private float perfectTolerance = 0.12f;
        [SerializeField, Min(0.1f)] private float cameraFollowSpeed = 4f;

        private readonly List<StackBlock> stack = new List<StackBlock>();
        private readonly List<GameObject> menuPreviewObjects = new List<GameObject>();
        private PhysicsMaterial blockPhysicsMaterial;
        private Material goldBarMaterial;
        private Mesh retainedGoldMesh;
        private StacklineEffectPool effectPool;
        private GameState state;
        private StackBlock movingBlock;
        private bool moveOnX;
        private int moveDirection;
        private int height;
        private int combo;
        private int perfects;
        private int gems;
        private int lives;
        private int stars;
        private bool usedRescue;
        private float cameraTargetY;
        private Vector3 cameraBasePosition;
        private Coroutine rescueRoutine;
        private int lastPlacementFrame = -1;
        private bool menuInteractionBlocked;
        private bool runCompleted;
        private StacklineProfileData profile;
        private bool leftControllerPressedLastFrame;
        private bool rightControllerPressedLastFrame;

        public GameState State => state;
        public int Height => height;
        public int Gems => gems;
        public int Lives => lives;
        public int Stars => stars;
        public int SelectedTheme => profile != null ? profile.selectedTheme : 0;
        public int ThemeCount => ThemeNames.Length;
        public int ChallengeCount => ChallengeGoals.Length;
        public bool AdFree => profile != null && profile.adFree;
        public bool SoundEnabled => profile == null || profile.soundEnabled;
        public bool HapticsEnabled => profile == null || profile.hapticsEnabled;
        public bool DailyBonusClaimed => profile != null && profile.dailyBonusDay == DateTime.Now.ToString("yyyyMMdd");
        public StacklineEffectPoolStats EffectPoolStats => effectPool != null ? effectPool.Stats : default;

        public bool ValidateEffectPool(out string problem)
        {
            if (effectPool != null) return effectPool.Validate(out problem);
            problem = "Effect pool is not initialized.";
            return false;
        }

        private sealed class StackBlock
        {
            public GameObject Object;
            public Renderer Renderer;
            public Vector3 Size;
            public Vector3 Position;
            public bool IsGolden;
        }

        public void Configure(Transform newArenaAnchor, Camera newCamera, StacklineHud newHud,
            StacklineTapTarget newTapTarget, GameObject newGoldBarPrefab)
        {
            xrMode = false;
            arenaAnchor = newArenaAnchor;
            gameplayCamera = newCamera;
            hud = newHud;
            tapTarget = newTapTarget;
            goldBarPrefab = newGoldBarPrefab;
            CacheCameraPose();
        }

        /// <summary>
        /// Routes camera ownership to the PICO tracked HMD. This deliberately does not move
        /// or reparent the user-authored gameplay hierarchy.
        /// </summary>
        public void ConfigureVr(Transform newArenaAnchor, Camera hmdCamera, StacklineHud newHud,
            StacklineTapTarget newTapTarget, GameObject newGoldBarPrefab)
        {
            xrMode = true;
            arenaAnchor = newArenaAnchor;
            gameplayCamera = hmdCamera;
            hud = newHud;
            tapTarget = newTapTarget;
            goldBarPrefab = newGoldBarPrefab;
        }

        private void Awake()
        {
            if (arenaAnchor == null)
                arenaAnchor = transform;
            if (gameplayCamera == null)
                gameplayCamera = GetComponentInChildren<Camera>(true);
            if (hud == null)
                hud = GetComponentInChildren<StacklineHud>(true);
            if (tapTarget == null)
                tapTarget = GetComponentInChildren<StacklineTapTarget>(true);

            if (tapTarget != null)
                tapTarget.Pressed += TryPlace;
            if (hud != null)
                hud.Configure(this, gameplayCamera, xrMode);

            if (!xrMode)
                StacklineFreeView.EnsureAttached(gameplayCamera);

            profile = StacklineProfileStore.Load();
            gems = profile.gems;
            lives = profile.lives;
            stars = profile.stars;
            hud?.SetTheme(GetThemePreviewColor(profile.selectedTheme));
            if (profile.runCount == 0 && profile.recentScores.Count == 0 && (gems != 2 || lives != 0))
            {
                gems = 2;
                lives = 0;
                SaveProfile();
            }
            blockPhysicsMaterial = CreatePhysicsMaterial();
            goldBarMaterial = StacklineGoldVisual.CreateTunedMaterial(goldBarPrefab);
            if (goldBarMaterial != null)
                retainedGoldMesh = StacklineGoldVisual.AcquireIngotMesh();
            else
                goldBarMaterial = CreateMaterial(new Color(0.83f, 0.56f, 0.12f), true);
            Color[] effectColors = new Color[ThemeCount];
            for (int i = 0; i < effectColors.Length; i++) effectColors[i] = GetThemePreviewColor(i);
            effectPool = new StacklineEffectPool(goldBarMaterial, retainedGoldMesh, effectColors, gameObject.layer);
            if (!xrMode)
            {
                CacheCameraPose();
                SetInitialCameraDistance();
            }
            ReturnToMenu();
        }

        private void OnDestroy()
        {
            if (tapTarget != null)
                tapTarget.Pressed -= TryPlace;
            ClearRunObjects();
            ClearMenuPreview();
            effectPool?.Dispose();
            effectPool = null;
            if (retainedGoldMesh != null)
                StacklineGoldVisual.ReleaseIngotMesh();
            retainedGoldMesh = null;
            if (goldBarMaterial != null)
                Destroy(goldBarMaterial);
            if (blockPhysicsMaterial != null)
                Destroy(blockPhysicsMaterial);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveProfile();
        }

        private void OnDisable()
        {
            // A disabled controller no longer ticks visual lifetimes. Return temporary
            // effects immediately rather than leaving frozen detached objects in the scene.
            effectPool?.Clear();
        }

        private void Update()
        {
            if (state == GameState.Playing && movingBlock != null)
                MoveBlock();

            // Do not overwrite the player's free camera transform while a run is active.
            effectPool?.Tick(Time.deltaTime, arenaAnchor.position.y - 12f);

            // Always sample both input paths.  Besides making controller edges independent of
            // a simultaneous mouse/keyboard event, it keeps a held controller button from
            // becoming a synthetic fresh press on the following frame.
            bool conventionalInputPressed = WasPressedThisFrame();
            bool controllerInputPressed = WasControllerPressedThisFrame();
            if (conventionalInputPressed || controllerInputPressed)
                HandlePrimaryInput();
            HandleReviveHotkeys();
        }

        public void BeginFromMenu()
        {
            if (state != GameState.Menu && state != GameState.GameOver && state != GameState.Ready)
                return;

            StartNewRun();
        }

        public void RestartRun()
        {
            StartNewRun();
        }

        public void ReturnToMenu()
        {
            bool preserveFinishedTower = state == GameState.GameOver && stack.Count > 0;
            StopRescue();
            if (!preserveFinishedTower)
            {
                ClearRunObjects();
                ClearMenuPreview();
                height = 0;
                combo = 0;
                perfects = 0;
                cameraTargetY = cameraBasePosition.y;
                CreateMenuPreview();
            }
            usedRescue = false;
            lastPlacementFrame = Time.frameCount;
            state = GameState.Menu;
            if (tapTarget != null)
                tapTarget.SetInteractable(false);
            int best = profile != null ? profile.bestScore : PlayerPrefs.GetInt(BestScoreKey, 0);
            hud?.ShowMenu(best, best, gems, lives, stars, false, false);
        }

        public void RequestRescue()
        {
            if (state != GameState.Revive || usedRescue)
                return;

            usedRescue = true;
            rescueRoutine = StartCoroutine(RescueCountdown());
        }

        public void ReviveWithLife()
        {
            if (state != GameState.Revive || usedRescue || lives < 1)
                return;

            lives--;
            SaveProfile();
            usedRescue = true;
            RestoreRunAfterRescue();
        }

        public void FinishGame()
        {
            if (state != GameState.Revive && state != GameState.Failing && state != GameState.Rescue)
                return;

            if (runCompleted)
                return;

            StopRescue();
            runCompleted = true;
            int previousBest = profile != null ? profile.bestScore : PlayerPrefs.GetInt(BestScoreKey, 0);
            bool isNewRecord = height > previousBest;
            int best = Mathf.Max(previousBest, height);
            if (profile != null)
            {
                profile.bestScore = best;
                profile.runCount++;
                profile.recentScores.Insert(0, height);
                if (profile.recentScores.Count > StacklineProfileStore.RecentScoreLimit)
                    profile.recentScores.RemoveRange(StacklineProfileStore.RecentScoreLimit,
                        profile.recentScores.Count - StacklineProfileStore.RecentScoreLimit);
            }
            SaveProfile();
            state = GameState.GameOver;
            if (tapTarget != null)
                tapTarget.SetInteractable(false);
            hud?.ShowGameOver(height, best, gems, lives, stars, isNewRecord);
        }

        public void TryPlace()
        {
            if (state != GameState.Playing || movingBlock == null || menuInteractionBlocked)
                return;
            if (lastPlacementFrame == Time.frameCount)
                return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            lastPlacementFrame = Time.frameCount;
            ResolvePlacement();
        }

        private void StartNewRun()
        {
            StopAllCoroutines();
            rescueRoutine = null;
            ClearRunObjects();
            ClearMenuPreview();
            height = 0;
            combo = 0;
            perfects = 0;
            usedRescue = false;
            runCompleted = false;
            menuInteractionBlocked = false;
            lastPlacementFrame = Time.frameCount;
            state = GameState.Ready;
            cameraTargetY = cameraBasePosition.y;
            CreateFoundation();
            SpawnMovingBlock();
            state = GameState.Playing;
            if (tapTarget != null)
                tapTarget.SetInteractable(true);
            hud?.ShowPlaying(height, gems, lives, stars);
        }

        private void CreateFoundation()
        {
            Vector3 position = new Vector3(0f, BlockHeight * 0.5f, 0f);
            StackBlock foundation = CreateBlock("Foundation", position, new Vector3(InitialWidth, BlockHeight, InitialDepth), GetBlockColor(0, false), false);
            stack.Add(foundation);
        }

        private void SpawnMovingBlock()
        {
            if (stack.Count == 0)
                return;

            StackBlock top = stack[stack.Count - 1];
            int nextFloor = height + 1;
            moveOnX = height % 2 == 0;
            bool golden = false;
            float riskScale = 1f;
            Vector3 size = new Vector3(top.Size.x * riskScale, BlockHeight, top.Size.z * riskScale);
            Vector3 position = top.Position + new Vector3(0f,
                (top.Size.y + size.y) * 0.5f + StackContactClearance, 0f);
            if (moveOnX)
                position.x = top.Position.x - movementRange;
            else
                position.z = top.Position.z - movementRange;

            movingBlock = CreateBlock("Moving Block", position, size, GetBlockColor(nextFloor, golden), golden);
            moveDirection = 1;
            UpdateCameraTarget(position.y + 0.9f);
            hud?.ShowPlaying(height, gems, lives, stars);
        }

        private void MoveBlock()
        {
            StackBlock top = stack[stack.Count - 1];
            float speed = Mathf.Min(baseSpeed + height * 0.07f, 5.4f);
            if (height > 0 && height % 10 == 0)
                speed *= 0.78f;

            Vector3 position = movingBlock.Object.transform.localPosition;
            float center = moveOnX ? top.Position.x : top.Position.z;
            float coordinate = moveOnX ? position.x : position.z;
            coordinate += moveDirection * speed * Time.deltaTime;
            float low = center - movementRange;
            float high = center + movementRange;
            if (coordinate >= high)
            {
                coordinate = high;
                moveDirection = -1;
            }
            else if (coordinate <= low)
            {
                coordinate = low;
                moveDirection = 1;
            }

            if (moveOnX)
                position.x = coordinate;
            else
                position.z = coordinate;
            // Blocks live in arena-local coordinates so the user's placement, rotation and
            // scale of the whole game stay intact.  Assigning world position here would throw
            // every moving bar away from the tower as soon as the root is transformed.
            movingBlock.Object.transform.localPosition = position;
            movingBlock.Position = position;
        }

        private void ResolvePlacement()
        {
            state = GameState.Resolving;
            StackBlock top = stack[stack.Count - 1];
            Vector3 movingPosition = movingBlock.Object.transform.localPosition;
            movingBlock.Position = movingPosition;
            float previousExtent = moveOnX ? top.Size.x : top.Size.z;
            float movingExtent = moveOnX ? movingBlock.Size.x : movingBlock.Size.z;
            float previousCenter = moveOnX ? top.Position.x : top.Position.z;
            float movingCenter = moveOnX ? movingPosition.x : movingPosition.z;
            float previousMin = previousCenter - previousExtent * 0.5f;
            float previousMax = previousCenter + previousExtent * 0.5f;
            float movingMin = movingCenter - movingExtent * 0.5f;
            float movingMax = movingCenter + movingExtent * 0.5f;
            float overlapMin = Mathf.Max(previousMin, movingMin);
            float overlapMax = Mathf.Min(previousMax, movingMax);
            float overlap = overlapMax - overlapMin;

            if (overlap <= MinimumExtent)
            {
                FailCurrentBlock();
                return;
            }

            float offset = movingCenter - previousCenter;
            bool perfect = Mathf.Abs(offset) <= Mathf.Min(perfectTolerance, movingExtent * 0.08f);
            Vector3 placedSize = movingBlock.Size;
            Vector3 placedPosition = movingPosition;
            bool hasCutPiece = false;
            Vector3 cutPosition = Vector3.zero;
            Vector3 cutSize = Vector3.zero;

            if (perfect)
            {
                if (moveOnX)
                    placedPosition.x = previousCenter;
                else
                    placedPosition.z = previousCenter;
                combo++;
                perfects++;
                float expansion = combo >= 3 ? Mathf.Min(0.06f, 0.012f + combo * 0.006f) : 0f;
                placedSize.x = Mathf.Min(InitialWidth, placedSize.x + expansion);
                placedSize.z = Mathf.Min(InitialDepth, placedSize.z + expansion);
                CreatePerfectBurst(placedPosition);
                CreatePerfectOutline(placedPosition, placedSize);
                hud?.ShowPerfect(combo);
            }
            else
            {
                combo = 0;
                placedSize = moveOnX
                    ? new Vector3(overlap, movingBlock.Size.y, movingBlock.Size.z)
                    : new Vector3(movingBlock.Size.x, movingBlock.Size.y, overlap);
                float placedCenter = (overlapMin + overlapMax) * 0.5f;
                if (moveOnX)
                    placedPosition.x = placedCenter;
                else
                    placedPosition.z = placedCenter;

                float cutExtent = movingExtent - overlap;
                cutSize = movingBlock.Size;
                if (moveOnX)
                    cutSize.x = cutExtent;
                else
                    cutSize.z = cutExtent;
                cutPosition = movingPosition;
                float cutCenter = offset > 0f ? overlapMax + cutExtent * 0.5f : overlapMin - cutExtent * 0.5f;
                if (moveOnX)
                    cutPosition.x = cutCenter;
                else
                    cutPosition.z = cutCenter;
                hasCutPiece = cutExtent > MinimumExtent;
            }

            ResizeBlock(movingBlock, placedPosition, placedSize);
            // The full moving block used to still occupy the cut area when the debris was
            // created.  That starts two colliders in the same space and can kick the cut piece
            // upward through the tower.  Resize first, then release the debris outboard.
            if (hasCutPiece)
                CreateFallingPiece(cutPosition, cutSize, offset);
            stack.Add(movingBlock);
            bool isGolden = movingBlock.IsGolden;
            movingBlock = null;
            height++;
            if (height % 10 == 0)
            {
                int reward = AdFree ? 2 : 1;
                gems += reward;
                SaveProfile();
                hud?.ShowGemBonus(reward);
            }
            UpdateCameraTarget(placedPosition.y + 1.3f);
            hud?.ShowPlaying(height, gems, lives, stars);
            StartCoroutine(SpawnAfterResolve());
        }

        private IEnumerator SpawnAfterResolve()
        {
            yield return new WaitForSeconds(0.1f);
            if (state != GameState.Resolving)
                yield break;

            SpawnMovingBlock();
            state = GameState.Playing;
        }

        private void FailCurrentBlock()
        {
            StackBlock failed = movingBlock;
            movingBlock = null;
            combo = 0;
            Vector3 localVelocity = moveOnX
                ? new Vector3(moveDirection * 1.3f, 0.15f, 0f)
                : new Vector3(0f, 0.15f, moveDirection * 1.3f);
            Vector3 velocity = arenaAnchor.TransformVector(localVelocity);
            MakeFalling(failed, velocity);
            state = GameState.Failing;
            if (tapTarget != null)
                tapTarget.SetInteractable(false);
            StartCoroutine(OfferReviveAfterFall());
        }

        private IEnumerator OfferReviveAfterFall()
        {
            yield return new WaitForSeconds(0.55f);
            if (state != GameState.Failing)
                yield break;

            if (usedRescue)
                FinishGame();
            else
            {
                state = GameState.Revive;
                hud?.ShowRevive(height, lives, gems);
            }
        }

        private IEnumerator RescueCountdown()
        {
            state = GameState.Rescue;
            for (int remaining = 3; remaining > 0; remaining--)
            {
                hud?.ShowRescue(remaining);
                yield return new WaitForSeconds(1f);
            }
            RestoreRunAfterRescue();
        }

        private void RestoreRunAfterRescue()
        {
            StopRescue();
            if (stack.Count == 0)
            {
                FinishGame();
                return;
            }

            StackBlock top = stack[stack.Count - 1];
            Vector3 safeSize = new Vector3(
                Mathf.Min(InitialWidth, top.Size.x + 0.22f),
                top.Size.y,
                Mathf.Min(InitialDepth, top.Size.z + 0.22f));
            ResizeBlock(top, top.Position, safeSize);
            combo = 0;
            CreatePerfectBurst(top.Position);
            state = GameState.Resolving;
            StartCoroutine(SpawnAfterResolve());
        }

        public void SetMenuInteractionBlocked(bool blocked)
        {
            menuInteractionBlocked = blocked;
            if (tapTarget != null && state == GameState.Playing)
                tapTarget.SetInteractable(!blocked);
        }

        public void ToggleSound()
        {
            EnsureProfile();
            profile.soundEnabled = !profile.soundEnabled;
            SaveProfile();
        }

        public void ToggleHaptics()
        {
            EnsureProfile();
            profile.hapticsEnabled = !profile.hapticsEnabled;
            SaveProfile();
        }

        public string GetChallengeLabel(int index)
        {
            if (index < 0 || index >= ChallengeGoals.Length)
                return string.Empty;
            return "REACH " + ChallengeGoals[index] + " FLOORS";
        }

        public bool IsChallengeClaimed(int index)
        {
            if (profile == null || index < 0 || index >= ChallengeGoals.Length)
                return false;
            return (profile.challengeClaims & (1 << index)) != 0;
        }

        public bool CanClaimChallenge(int index)
        {
            return index >= 0 && index < ChallengeGoals.Length && profile != null &&
                profile.bestScore >= ChallengeGoals[index] && !IsChallengeClaimed(index);
        }

        public string ClaimChallenge(int index)
        {
            if (!CanClaimChallenge(index))
                return IsChallengeClaimed(index) ? "ALREADY CLAIMED" : "KEEP STACKING";
            profile.challengeClaims |= 1 << index;
            stars++;
            SaveProfile();
            hud?.RefreshPersistentState();
            return "+1 STAR";
        }

        public string GetThemeName(int index)
        {
            return index >= 0 && index < ThemeNames.Length ? ThemeNames[index] : "BULLION";
        }

        public int GetThemeCost(int index)
        {
            return index >= 0 && index < ThemeCosts.Length ? ThemeCosts[index] : 0;
        }

        public bool IsThemeUnlocked(int index)
        {
            if (profile == null || index < 0 || index >= ThemeNames.Length)
                return index == 0;
            return (profile.unlockedThemeMask & (1 << index)) != 0;
        }

        public Color GetThemePreviewColor(int index)
        {
            Color[] colors =
            {
                new Color(0.93f, 0.68f, 0.20f), new Color(0.68f, 0.43f, 0.16f),
                new Color(1.00f, 0.79f, 0.31f), new Color(1.00f, 0.87f, 0.50f),
                new Color(0.84f, 0.54f, 0.43f), new Color(0.79f, 0.76f, 0.65f),
            };
            return colors[Mathf.Clamp(index, 0, colors.Length - 1)];
        }

        public string SelectOrUnlockTheme(int index)
        {
            if (index < 0 || index >= ThemeNames.Length)
                return "UNAVAILABLE";
            EnsureProfile();
            if (!IsThemeUnlocked(index))
            {
                int cost = GetThemeCost(index);
                if (gems < cost)
                    return "NEED " + cost + " GOLD";
                gems -= cost;
                profile.unlockedThemeMask |= 1 << index;
            }
            profile.selectedTheme = index;
            SaveProfile();
            hud?.SetTheme(GetThemePreviewColor(index));
            hud?.RefreshPersistentState();
            return GetThemeName(index) + " SELECTED";
        }

        public string[] GetLeaderboardLines()
        {
            EnsureProfile();
            List<string> rows = new List<string>();
            int[] showcase = { 51, 37, 27, 16, 10 };
            int rank = 1;
            foreach (int score in showcase)
            {
                rows.Add(rank + ".  GOLD HUNTER                 " + score);
                rank++;
            }
            if (profile.recentScores.Count == 0)
                rows.Add(rank + ".  YOU                         " + profile.bestScore);
            else
            {
                foreach (int score in profile.recentScores)
                {
                    rows.Add(rank + ".  YOU                         " + score);
                    rank++;
                }
            }
            return rows.ToArray();
        }

        public string ActivateAdFree()
        {
            EnsureProfile();
            if (profile.adFree)
                return "AD-FREE ACTIVE";
            const int cost = 5;
            if (gems < cost)
                return "NEED 5 GOLD";
            gems -= cost;
            profile.adFree = true;
            profile.doubleGems = true;
            SaveProfile();
            hud?.RefreshPersistentState();
            return "HEIGHT REWARDS x2";
        }

        public string ClaimDailyBonus()
        {
            EnsureProfile();
            string today = DateTime.Now.ToString("yyyyMMdd");
            if (profile.dailyBonusDay == today)
                return "COME BACK TOMORROW";
            profile.dailyBonusDay = today;
            gems += 2;
            SaveProfile();
            hud?.RefreshPersistentState();
            return "+2 GOLD";
        }

        public string BuyLives(int amount, int cost)
        {
            if (amount <= 0 || cost < 0)
                return "UNAVAILABLE";
            EnsureProfile();
            if (gems < cost)
                return "NOT ENOUGH GOLD";
            gems -= cost;
            lives += amount;
            SaveProfile();
            hud?.RefreshPersistentState();
            return "+" + amount + (amount == 1 ? " LIFE" : " LIVES");
        }

        private void EnsureProfile()
        {
            if (profile == null)
                profile = StacklineProfileStore.Load();
        }

        private void SaveProfile()
        {
            EnsureProfile();
            profile.gems = Mathf.Max(0, gems);
            profile.lives = Mathf.Max(0, lives);
            profile.stars = Mathf.Max(0, stars);
            StacklineProfileStore.Save(profile);
        }

        private void StopRescue()
        {
            if (rescueRoutine != null)
            {
                StopCoroutine(rescueRoutine);
                rescueRoutine = null;
            }
        }

        private void CreateMenuPreview()
        {
            if (menuPreviewObjects.Count > 0 || arenaAnchor == null)
                return;

            float width = InitialWidth * 1.08f;
            for (int index = 0; index < 14; index++)
            {
                float taper = Mathf.Lerp(1f, 0.68f, index / 13f);
                float wobble = Mathf.Sin(index * 1.21f) * 0.12f;
                Vector3 position = new Vector3(
                    index % 2 == 0 ? wobble : -wobble,
                    BlockHeight * (index + 0.5f),
                    Mathf.Cos(index * 0.83f) * 0.09f);
                GameObject preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                preview.name = "Menu Tower Preview " + index;
                preview.layer = gameObject.layer;
                preview.transform.SetParent(arenaAnchor, false);
                preview.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
                Collider collider = preview.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
                Renderer fallbackRenderer = preview.GetComponent<Renderer>();
                Renderer renderer = StacklineGoldVisual.Attach(preview, goldBarPrefab, goldBarMaterial,
                    gameObject.layer);
                if (renderer == null)
                    fallbackRenderer.sharedMaterial = goldBarMaterial;
                preview.transform.localScale = new Vector3(width * taper, BlockHeight, width * taper);
                menuPreviewObjects.Add(preview);
            }
            UpdateCameraTarget(BlockHeight * 14f + 1.2f);
        }

        private void ClearMenuPreview()
        {
            foreach (GameObject preview in menuPreviewObjects)
            {
                if (preview != null)
                    Destroy(preview);
            }
            menuPreviewObjects.Clear();
        }

        private StackBlock CreateBlock(string name, Vector3 position, Vector3 size, Color color, bool golden)
        {
            GameObject blockObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blockObject.name = name;
            blockObject.layer = gameObject.layer;
            blockObject.transform.SetParent(arenaAnchor, false);
            blockObject.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
            Renderer fallbackRenderer = blockObject.GetComponent<Renderer>();
            Renderer renderer = StacklineGoldVisual.Attach(blockObject, goldBarPrefab, goldBarMaterial,
                gameObject.layer);
            if (renderer == null)
            {
                fallbackRenderer.sharedMaterial = goldBarMaterial;
                // Keep StackBlock.Renderer valid for cut pieces when the optional GLB cannot be imported.
                renderer = fallbackRenderer;
            }
            blockObject.transform.localScale = size;
            BoxCollider collider = blockObject.GetComponent<BoxCollider>();
            // Keep gameplay collision based on the requested placement footprint rather than
            // the tapered decorative mesh.  This makes every placed ingot meet cleanly edge to
            // edge at the same height.
            collider.center = Vector3.zero;
            collider.size = Vector3.one;
            collider.sharedMaterial = GetPhysicsMaterial();
            return new StackBlock
            {
                Object = blockObject,
                Renderer = renderer,
                Position = position,
                Size = size,
                IsGolden = golden,
            };
        }

        private void ResizeBlock(StackBlock block, Vector3 position, Vector3 size)
        {
            block.Position = position;
            block.Size = size;
            block.Object.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
            block.Object.transform.localScale = size;
        }

        private void CreateFallingPiece(Vector3 position, Vector3 size, float offset)
        {
            float side = Mathf.Sign(offset);
            if (Mathf.Approximately(side, 0f))
                side = moveDirection == 0 ? 1f : Mathf.Sign(moveDirection);

            // Start the trimmed piece just beyond the supporting footprint and give it a clear
            // sideways/downward impulse. The visual pool has no colliders, so it cannot climb
            // back onto the tower or intercept later placements while it is visible falling.
            Vector3 outward = moveOnX ? new Vector3(side, 0f, 0f) : new Vector3(0f, 0f, side);
            position += outward * (0.08f + (moveOnX ? size.x : size.z) * 0.08f);
            position += Vector3.down * 0.035f;
            Vector3 velocity = arenaAnchor.TransformVector(outward * 3.1f + Vector3.down * 0.40f);
            effectPool?.SpawnFragment(arenaAnchor, position, Quaternion.identity, size, velocity);
        }

        private void MakeFalling(StackBlock block, Vector3 velocity)
        {
            if (block == null || block.Object == null)
                return;

            Transform source = block.Object.transform;
            effectPool?.SpawnFragment(source.parent, source.localPosition, source.localRotation,
                source.localScale, velocity);
            // Gameplay blocks are never adopted into the effect pool. In particular, no
            // object with an outstanding delayed Destroy can later be reused by the pool.
            block.Object.SetActive(false);
            Destroy(block.Object);
        }

        private void CreatePerfectBurst(Vector3 position)
        {
            Vector3 worldPosition = arenaAnchor.TransformPoint(position + Vector3.up * (BlockHeight * 0.7f));
            effectPool?.SpawnPerfect(transform, worldPosition, SelectedTheme);
        }

        private void CreatePerfectOutline(Vector3 position, Vector3 size)
        {
            effectPool?.SpawnOutline(arenaAnchor, position, size);
        }

        private static Material CreateMaterial(Color color, bool golden)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", golden ? 0.50f : 0.32f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", golden ? 0.90f : 0.02f);
            if (material.HasProperty("_EmissionColor"))
            {
                if (golden)
                {
                    material.DisableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", Color.black);
                }
                else
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", color * 0.13f);
                }
            }
            return material;
        }

        private static PhysicsMaterial CreatePhysicsMaterial()
        {
            PhysicsMaterial material = new PhysicsMaterial("Stackline Grip")
            {
                staticFriction = 0.85f,
                dynamicFriction = 0.72f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            return material;
        }

        private PhysicsMaterial GetPhysicsMaterial()
        {
            if (blockPhysicsMaterial == null)
                blockPhysicsMaterial = CreatePhysicsMaterial();
            return blockPhysicsMaterial;
        }

        private Color GetBlockColor(int floor, bool golden)
        {
            if (golden)
                return new Color(1f, 0.68f, 0.16f);

            Color seed = GetThemePreviewColor(SelectedTheme);
            Color.RGBToHSV(seed, out float hue, out float saturation, out float value);
            float phase = Mathf.Repeat(floor / 54f, 1f);
            hue = Mathf.Repeat(hue - phase * 0.33f, 1f);
            saturation = Mathf.Lerp(Mathf.Clamp01(saturation * 0.72f), Mathf.Clamp01(saturation),
                0.5f + 0.5f * Mathf.Sin(floor * 0.13f));
            value = Mathf.Lerp(Mathf.Clamp01(value * 0.68f), Mathf.Clamp01(value),
                0.5f + 0.5f * Mathf.Sin(floor * 0.09f + 0.4f));
            return Color.HSVToRGB(hue, saturation, value);
        }

        private void CacheCameraPose()
        {
            if (gameplayCamera == null)
                return;
            cameraBasePosition = gameplayCamera.transform.position;
            cameraTargetY = gameplayCamera.transform.position.y;
        }

        private void SetInitialCameraDistance()
        {
            if (gameplayCamera == null || arenaAnchor == null || !gameplayCamera.orthographic)
                return;

            // This game root was intentionally enlarged in the scene.  Orthographic size is
            // expressed in world units, so compensate for that scale once at startup.  The
            // player can still freely zoom farther with the mouse wheel afterwards.
            float worldVerticalScale = Mathf.Max(0.01f, Mathf.Abs(arenaAnchor.lossyScale.y));
            float menuPreviewHeight = BlockHeight * 14f + 1.2f;
            float comfortableSize = menuPreviewHeight * worldVerticalScale * 0.5f;
            gameplayCamera.orthographicSize = Mathf.Max(gameplayCamera.orthographicSize,
                Mathf.Clamp(comfortableSize, 8.5f, 18f));
        }

        private void UpdateCameraTarget(float stackTopLocalY)
        {
            if (arenaAnchor == null || gameplayCamera == null)
                return;

            // Keep the base visible, then lift the camera and aim point together as the tower grows.
            float stackTopY = arenaAnchor.TransformPoint(Vector3.up * stackTopLocalY).y;
            float rise = Mathf.Max(0f, stackTopY - (arenaAnchor.position.y + 2.8f));
            cameraTargetY = Mathf.Max(cameraTargetY, cameraBasePosition.y + rise);
        }

        private void FollowCamera()
        {
            if (gameplayCamera == null || arenaAnchor == null)
                return;

            Vector3 current = gameplayCamera.transform.position;
            float targetY = Mathf.Max(cameraBasePosition.y, cameraTargetY);
            float y = Mathf.Lerp(current.y, targetY, 1f - Mathf.Exp(-cameraFollowSpeed * Time.deltaTime));
            Vector3 targetPosition = new Vector3(cameraBasePosition.x, y, cameraBasePosition.z);
            gameplayCamera.transform.position = targetPosition;
            float rise = Mathf.Max(0f, targetY - cameraBasePosition.y);
            Vector3 lookTarget = arenaAnchor.position + Vector3.up * (1.0f + rise);
            gameplayCamera.transform.rotation = Quaternion.Slerp(gameplayCamera.transform.rotation,
                Quaternion.LookRotation(lookTarget - targetPosition, Vector3.up), 1f - Mathf.Exp(-cameraFollowSpeed * Time.deltaTime));
        }

        private void ClearRunObjects()
        {
            if (movingBlock != null && movingBlock.Object != null)
                Destroy(movingBlock.Object);
            movingBlock = null;
            foreach (StackBlock block in stack)
            {
                if (block != null && block.Object != null)
                    Destroy(block.Object);
            }
            stack.Clear();
            effectPool?.Clear();
        }

        private static bool WasPressedThisFrame()
        {
            bool mouse = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool touch = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
            bool keyboard = Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame);
            return mouse || touch || keyboard;
        }

        /// <summary>
        /// Desktop retains its broad controller fallback. In VR, the primary gameplay action
        /// is intentionally restricted to the right trigger so navigation buttons, left-hand
        /// interactions, and thumbstick clicks can never accidentally drop a gold bar.
        /// </summary>
        private bool WasControllerPressedThisFrame()
        {
            bool leftPressed = !xrMode && ReadControllerPrimaryPress(UnityEngine.XR.XRNode.LeftHand, false);
            bool rightPressed = ReadControllerPrimaryPress(UnityEngine.XR.XRNode.RightHand, xrMode);
            bool pressedThisFrame = (leftPressed && !leftControllerPressedLastFrame) ||
                (rightPressed && !rightControllerPressedLastFrame);
            leftControllerPressedLastFrame = leftPressed;
            rightControllerPressedLastFrame = rightPressed;
            return pressedThisFrame;
        }

        private static bool ReadControllerPrimaryPress(UnityEngine.XR.XRNode node, bool triggerOnly)
        {
            UnityEngine.XR.InputDevice device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;

            bool pressed;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out pressed) && pressed)
                return true;
            if (triggerOnly)
                return false;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out pressed) && pressed)
                return true;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out pressed) && pressed)
                return true;
            return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out pressed) && pressed;
        }

        private void HandlePrimaryInput()
        {
            if (menuInteractionBlocked || (hud != null && hud.CapturesPrimaryInput))
                return;
            if (state == GameState.Menu || state == GameState.Ready)
            {
                if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                    StartNewRun();
                return;
            }

            if (state == GameState.GameOver)
            {
                if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                    StartNewRun();
                return;
            }

            TryPlace();
        }

        private void HandleReviveHotkeys()
        {
            if (state != GameState.Revive || Keyboard.current == null)
                return;

            if (Keyboard.current.rKey.wasPressedThisFrame)
                RequestRescue();
            else if (Keyboard.current.lKey.wasPressedThisFrame)
                ReviveWithLife();
            else if (Keyboard.current.escapeKey.wasPressedThisFrame)
                FinishGame();
        }
    }
}
