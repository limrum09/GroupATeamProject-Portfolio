using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
static class SoundPlaybackVerification
{
    const string Running = "SoundPlaybackVerification.Running";
    const string Run = "Assets/_Project/01.Scene/TestScenes/PlayerGameTestScene.unity";
    const string Lobby = "Assets/_Project/01.Scene/TestScenes/TestLobby_Menu.unity";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly float[] Samples = new float[256];
    static int _failures;
    static IEnumerator _steps;
    static double _deadline;

    static SoundPlaybackVerification()
    {
        EditorApplication.playModeStateChanged += OnPlayState;
    }

    /// <summary>
    /// 명령줄에서 사운드 재생 검증용 Play Mode 시작
    /// </summary>
    public static void Start()
    {
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Run);
        SessionState.SetBool(Running, true);
        Report("START");
        EditorApplication.delayCall += EditorApplication.EnterPlaymode;
    }

    static void OnPlayState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Running, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _steps = Verify().GetEnumerator();
            _deadline = EditorApplication.timeSinceStartup + 150;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseBool(Running);
            EditorApplication.Exit(SessionState.GetInt(Running, 1));
        }
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > _deadline)
                throw new TimeoutException("Playback verification timed out.");
            if (_steps.MoveNext()) return;
        }
        catch (Exception error)
        {
            Check(false, error.ToString());
        }
        EditorApplication.update -= Tick;
        SessionState.SetInt(Running, _failures == 0 ? 0 : 1);
        Report($"END failures={_failures}");
        EditorApplication.ExitPlaymode();
    }

    static void Report(string message) => Debug.Log($"[SoundPlaybackVerification] {message}");

    static void Check(bool success, string message)
    {
        if (!success) _failures++;
        Report($"{(success ? "PASS" : "FAIL")} {message}");
    }

    static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Fields).GetValue(owner);

    static bool Signal(AudioSource source)
    {
        source.GetOutputData(Samples, 0);
        return Samples.Any(sample => Mathf.Abs(sample) > 0.00001f);
    }

    static bool OutputSignal()
    {
        AudioListener.GetOutputData(Samples, 0);
        return Samples.Any(sample => Mathf.Abs(sample) > 0.00001f);
    }

    static bool Track(SoundPlayer player, SoundCue cue, Vector3? position, ref bool signal)
    {
        if (cue == null || player == null) return false;
        SoundCue[] cues = Read<SoundCue[]>(player, "_playingCues");
        AudioSource[] sources = Read<AudioSource[]>(player, "_sources");
        bool playing = false;
        for (int index = 0; index < sources.Length; index++)
        {
            AudioSource source = sources[index];
            if (cues[index] != cue || !source.isPlaying || source.outputAudioMixerGroup != cue.OutputGroup) continue;
            if (position.HasValue && (source.transform.position - position.Value).sqrMagnitude > 0.01f) continue;
            playing = true;
            signal |= Signal(source);
        }
        return playing;
    }

    static CombatTarget CreateTarget(CombatController combat)
    {
        GameObject root = new GameObject("Sound verification target");
        root.SetActive(false);
        root.layer = LayerMask.NameToLayer("Enemy");
        Vector3 position = combat.Owner.Position + combat.Owner.ActorTransform.forward * Mathf.Min(1.5f, combat.AttackRange * 0.75f);
        root.transform.position = position;
        GameObject aim = new GameObject("Aim");
        aim.transform.SetParent(root.transform, false);
        aim.transform.position = position + combat.Owner.AimPosition - combat.Owner.Position;
        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.center = aim.transform.localPosition;
        collider.radius = 0.55f;
        collider.height = 2f;
        CharacterStatus status = root.AddComponent<CharacterStatus>();
        status.InitializeActor(combat.Owner.Status.Definition);
        CombatTarget target = root.AddComponent<CombatTarget>();
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty("_team").enumValueIndex = (int)CombatTeam.Enemy;
        serialized.FindProperty("_status").objectReferenceValue = status;
        serialized.FindProperty("_actorTransform").objectReferenceValue = root.transform;
        serialized.FindProperty("_aimPoint").objectReferenceValue = aim.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(true);
        Physics.SyncTransforms();
        return target;
    }

    static IEnumerable Verify()
    {
        PlayerActor previous = null;
        string previousCharacter = null;
        for (int index = 0; index < 2; index++)
        {
            PlayerActor actor = null;
            double readyBy = EditorApplication.timeSinceStartup + 15;
            while (EditorApplication.timeSinceStartup < readyBy)
            {
                actor = Object.FindFirstObjectByType<PlayerActor>();
                if (actor != null && actor != previous && actor.IsPrepared) break;
                yield return null;
            }
            if (actor == null || actor == previous || !actor.IsPrepared)
            {
                Check(false, $"Combat run {index + 1} did not prepare a player.");
                break;
            }

            string character = actor.Status.Definition.name;
            CombatSoundPresenter presenter = actor.GetComponentInChildren<CombatSoundPresenter>();
            Check(presenter != null && presenter.IsListening, $"{character} presenter subscribed");
            if (index == 1) Check(previous == null && character != previousCharacter, "Character switch removed previous actor");
            if (presenter != null && presenter.IsListening)
            {
                CombatController combat = actor.Loadout.Combat;
                SoundPlayer player = Object.FindFirstObjectByType<SoundPlayer>();
                Check(!combat.TryAttack(null), $"{character} invalid basic attack rejected");
                foreach (object step in Attack(combat, presenter, player, -1)) yield return step;
                for (int slot = 0; slot < actor.Loadout.SlotCount; slot++)
                {
                    if (actor.Loadout.GetSkill(slot) == null) continue;
                    foreach (object step in Attack(combat, presenter, player, slot)) yield return step;
                }
                double cooldown = EditorApplication.timeSinceStartup + 1.5;
                while (EditorApplication.timeSinceStartup < cooldown) yield return null;
                foreach (object step in Attack(combat, presenter, player, -1, true)) yield return step;
            }

            previous = actor;
            previousCharacter = character;
            if (index == 0)
            {
                RunSceneTestBootstrap bootstrap = Object.FindFirstObjectByType<RunSceneTestBootstrap>();
                if (bootstrap == null)
                {
                    Check(false, "Character switch bootstrap missing.");
                    break;
                }
                bootstrap.SwitchToNextCharacter();
            }
        }

        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(Lobby, new LoadSceneParameters(LoadSceneMode.Single));
        while (!load.isDone) yield return null;
        foreach (object step in VerifyLobby()) yield return step;
    }

    static IEnumerable Attack(CombatController combat, CombatSoundPresenter presenter, SoundPlayer player, int slot, bool miss = false)
    {
        string label = slot < 0 ? (miss ? "Basic attack miss" : "Basic attack") : $"Slot {slot + 1} {combat.Skills.GetSkill(slot).Definition.name}";
        double readyBy = EditorApplication.timeSinceStartup + 5;
        while (combat.IsBusy && EditorApplication.timeSinceStartup < readyBy) yield return null;
        SoundCue executionCue;
        SoundCue impactCue;
        if (slot < 0)
        {
            executionCue = Read<SoundCue>(presenter, "_basicExecutedCue");
            impactCue = Read<SoundCue>(presenter, "_basicImpactCue");
        }
        else
        {
            using SerializedObject serialized = new SerializedObject(presenter);
            SerializedProperty bindings = serialized.FindProperty("_skillSounds");
            SerializedProperty binding = null;
            for (int index = 0; index < bindings.arraySize; index++)
            {
                SerializedProperty entry = bindings.GetArrayElementAtIndex(index);
                if (entry.FindPropertyRelative("Skill").objectReferenceValue == combat.Skills.GetSkill(slot).Definition)
                    binding = entry;
            }
            executionCue = binding?.FindPropertyRelative("ExecutedCue").objectReferenceValue as SoundCue;
            impactCue = binding?.FindPropertyRelative("ImpactCue").objectReferenceValue as SoundCue;
        }

        CombatTarget target = CreateTarget(combat);
        int executed = 0;
        int hits = 0;
        int launchFrame = -1;
        int hitFrame = -1;
        Vector3? launchPosition = null;
        Vector3? hitPosition = null;
        void Observe(SkillExecutionContext execution)
        {
            execution.OnExecuted += (_, position) =>
            {
                executed++;
                launchFrame = Time.frameCount;
                launchPosition = position;
            };
            execution.OnImpact += impact =>
            {
                if (impact.Target != target) return;
                hits++;
                hitFrame = Time.frameCount;
                hitPosition = impact.Position;
            };
        }

        combat.OnExecutionCreated += Observe;
        bool accepted = slot < 0
            ? combat.TryAttack(target)
            : combat.TryUseSkill(slot, new SkillAimRequest((target.Position - combat.Owner.Position).normalized, target, target.Position, true));
        if (miss)
        {
            target.transform.position += Vector3.up * 100f;
            Physics.SyncTransforms();
        }
        bool launchSignal = false;
        bool hitSignal = false;
        bool launchPlaying = false;
        bool hitPlaying = false;
        bool output = false;
        double stop = EditorApplication.timeSinceStartup + (accepted ? 4 : 0);
        while (EditorApplication.timeSinceStartup < stop)
        {
            if (executed != 0) launchPlaying |= Track(player, executionCue, launchPosition, ref launchSignal);
            if (hits != 0 || (miss && executed != 0)) hitPlaying |= Track(player, impactCue, hitPosition, ref hitSignal);
            if (launchPlaying || hitPlaying) output |= OutputSignal();
            if (launchSignal && hitSignal && output) break;
            yield return null;
        }
        combat.OnExecutionCreated -= Observe;
        Object.Destroy(target.gameObject);
        Check(accepted && executed != 0 && launchPlaying, $"{label} execution accepted={accepted} events={executed} source={launchPlaying} samples={launchSignal}");
        Check(miss ? hits == 0 && !hitPlaying : hits != 0 && hitPlaying,
            $"{label} impact hits={hits} source={hitPlaying} samples={hitSignal} launchFrame={launchFrame} hitFrame={hitFrame}");
        Check(launchSignal && (miss || hitSignal) && output, $"{label} output execution={launchSignal} impact={hitSignal} listener={output}");
    }

    static IEnumerable VerifyLobby()
    {
        SoundPlayer player = Object.FindFirstObjectByType<SoundPlayer>();
        AmbientSoundPlayer[] emitters = Object.FindObjectsByType<AmbientSoundPlayer>(FindObjectsSortMode.None);
        foreach (AmbientSoundPlayer emitter in emitters)
        {
            AudioSource source = Read<AudioSource>(emitter, "_source");
            bool signal = false;
            double until = EditorApplication.timeSinceStartup + 1;
            while (!signal && EditorApplication.timeSinceStartup < until)
            {
                signal |= Signal(source);
                yield return null;
            }
            Check(source.isPlaying && source.loop && source.outputAudioMixerGroup != null && signal,
                $"{emitter.name} ambient loop playing={source.isPlaying} samples={signal}");
        }
        Check(emitters.Length >= 2, $"Lobby ambient emitters={emitters.Length}");
        foreach (AmbientSoundPlayer emitter in emitters) emitter.gameObject.SetActive(false);

        UIButtonSoundBindings bindings = Object.FindFirstObjectByType<UIButtonSoundBindings>();
        if (bindings == null || player == null)
        {
            Check(false, "Lobby UI audio bindings or sound player missing.");
            yield break;
        }
        foreach (string cueName in new[] { "UI_Click", "UI_Cancel" })
        {
            SoundCue cue = AssetDatabase.LoadAssetAtPath<SoundCue>($"Assets/_Project/06.Data/Audio/UI/{cueName}.asset");
            Button button = null;
            foreach (Button candidate in Read<Button[]>(bindings, "_subscribedButtons"))
            {
                if (candidate == null) continue;
                if (cueName == "UI_Click" && candidate.name == "SettingsButton"
                    && candidate.gameObject.activeInHierarchy && candidate.interactable)
                {
                    button = candidate;
                    break;
                }
                if (cueName != "UI_Cancel") continue;
                using SerializedObject serialized = new SerializedObject(bindings);
                SerializedProperty overrides = serialized.FindProperty("_bindings");
                for (int index = 0; index < overrides.arraySize; index++)
                {
                    SerializedProperty entry = overrides.GetArrayElementAtIndex(index);
                    if (entry.FindPropertyRelative("Button").objectReferenceValue == candidate
                        && entry.FindPropertyRelative("Cue").objectReferenceValue == cue)
                    {
                        button = candidate;
                        break;
                    }
                }
                if (button != null) break;
            }
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                Check(false, $"{cueName} button unavailable.");
                continue;
            }
            string buttonName = button.name;
            button.onClick.Invoke();
            bool signal = false;
            bool playing = false;
            bool output = false;
            double until = EditorApplication.timeSinceStartup + 0.5;
            while (EditorApplication.timeSinceStartup < until)
            {
                playing |= Track(player, cue, null, ref signal);
                if (playing) output |= OutputSignal();
                if (signal && output) break;
                yield return null;
            }
            Check(playing && signal && output, $"{cueName} button={buttonName} source={playing} sourceSamples={signal} listenerSignal={output}");
        }
    }
}
