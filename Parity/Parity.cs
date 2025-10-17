using Beatmap.Base;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace Parity
{
    [Plugin("Parity")]
    public class Parity
    {
        // Constants
        private const int ColorRed = 0;
        private const int ColorBlue = 1;
        private const string ParityFileName = "parity.txt";
        private const int EditorSceneBuildIndex = 3;
        private const int InitialWaitMilliseconds = 30000; // total max wait
        private const int PollIntervalMilliseconds = 1000; // poll interval
        private const float HsvAdjust = 0.4f;
        private const float DefaultEventCooldown = 0.25f;
        private const float HoldDurationSeconds = 1f;

        private BeatSaberSongContainer? _beatSaberSongContainer;
        private NoteGridContainer? _noteGridContainer;
        private AudioTimeSyncController? _audioTimeSyncController;
        private string? _folderPath;
        private List<ParityData> _parityData = new List<ParityData>();
        private InputAction? _flagParityAction;
        private InputAction? _unflagParityAction;
        private float lastEventTime = -Mathf.Infinity;
        private float eventCooldown = DefaultEventCooldown;
        public static event Action SavingEvent;
        private Dictionary<BaseNote, Color> _noteColors = new Dictionary<BaseNote, Color>();
        private static Harmony harmony;
        private bool initParity;

        private static readonly int _colorId = Shader.PropertyToID("_Color");

        [Init]
        private void Init()
        {
            harmony = new Harmony("Loloppe.ChroMapper.Parity");

            SceneManager.sceneLoaded += SceneLoaded;
            BeatmapActionContainer.ActionCreatedEvent += OnBeatmapActionCreated;
            BeatmapActionContainer.ActionUndoEvent += OnBeatmapActionUndo;
            BeatmapActionContainer.ActionRedoEvent += OnBeatmapActionRedo;
            SavingEvent += OnSavingEvent;

            _flagParityAction = new InputAction("Flag Parity", type: InputActionType.Button, binding: "<Keyboard>/f1");
            _flagParityAction.performed += OnFlagPerformed;
            _flagParityAction.Enable();

            _unflagParityAction = new InputAction("Unflag Parity", type: InputActionType.Button);
            _unflagParityAction.AddBinding("<Keyboard>/f1").WithInteraction($"hold(duration={HoldDurationSeconds})");
            _unflagParityAction.performed += OnUnflagPerformed;
            _unflagParityAction.Enable();

            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private async void SceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            if (arg0.buildIndex == EditorSceneBuildIndex)
            {
                _parityData.Clear();
                _noteColors.Clear();
                initParity = true;

                // Poll for required objects with a shorter total wait instead of a fixed long delay
                int waited = 0;
                _noteGridContainer = null;
                _beatSaberSongContainer = null;
                _audioTimeSyncController = null;

                while ((_noteGridContainer == null || _beatSaberSongContainer == null || _audioTimeSyncController == null) && waited < InitialWaitMilliseconds)
                {
                    await Task.Delay(PollIntervalMilliseconds);
                    waited += PollIntervalMilliseconds;
                    _noteGridContainer = _noteGridContainer ?? Object.FindObjectOfType<NoteGridContainer>();
                    _beatSaberSongContainer = _beatSaberSongContainer ?? Object.FindObjectOfType<BeatSaberSongContainer>();
                    _audioTimeSyncController = _audioTimeSyncController ?? Object.FindObjectOfType<AudioTimeSyncController>();
                }

                if (_audioTimeSyncController != null)
                {
                    _audioTimeSyncController.TimeChanged += OnTimeChanged;
                }

                if (_beatSaberSongContainer != null)
                    _folderPath = _beatSaberSongContainer.Info.Directory;

                var notes = _noteGridContainer.MapObjects.OrderBy(o => o.JsonTime).ToList();
                Helper.HandlePattern(notes);

                LoadParityFile();
            }
        }

        public static void InvokeSavingEvent()
        {
            SavingEvent?.Invoke();
        }

        private void OnSavingEvent() => SaveParity();

        private void SaveParity()
        {
            if (string.IsNullOrEmpty(_folderPath)) return;

            try
            {
                var filePath = Path.Combine(_folderPath, ParityFileName);
                string jsonString = JsonConvert.SerializeObject(_parityData.Where(x => x.ManuallyTagged));
                File.WriteAllText(filePath, jsonString);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save parity: {ex}");
            }
        }

        private void LoadParityFile()
        {
            if (string.IsNullOrEmpty(_folderPath)) return;

            var filePath = Path.Combine(_folderPath, ParityFileName);

            if (File.Exists(filePath))
            {
                try
                {
                    string text = File.ReadAllText(filePath);
                    _parityData = JsonConvert.DeserializeObject<List<ParityData>>(text) ?? new List<ParityData>();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to load parity data: {ex}");
                    _parityData = new List<ParityData>();
                }
            }
        }

        private void FlagParityEvent()
        {
            var selection = SelectionController.SelectedObjects;
            if (selection.Count > 0 && selection.All(x => x is BaseNote))
            {
                var select = new List<BaseNote>(selection.Cast<BaseNote>());
                foreach (var note in select)
                {
                    var data = _parityData.FirstOrDefault(d => d.JsonTime == note.JsonTime && d.Color == note.color && d.PosX == note.PosX && d.PosY == note.PosY);
                    if (data != null)
                    {
                        data.ManuallyTagged = true;
                        data.IsForehand = !data.IsForehand;
                    }
                }
            }

            UpdateParity();
        }

        private void UnflagParityEvent()
        {
            var selection = SelectionController.SelectedObjects;
            if (selection.Count > 0 && selection.All(x => x is BaseNote))
            {
                var select = new List<BaseNote>(selection.Cast<BaseNote>());
                foreach (var note in select)
                {
                    var data = _parityData.FirstOrDefault(d => d.JsonTime == note.JsonTime && d.Color == note.color && d.PosX == note.PosX && d.PosY == note.PosY);
                    if (data != null)
                    {
                        data.ManuallyTagged = false;
                    }
                }
            }

            UpdateParity();
        }

        private void UpdateParity(bool limiter = false)
        {
            if (!limiter || Time.time >= lastEventTime + eventCooldown)
            {
                if (_noteGridContainer == null) return;

                // Clear out colors
                ResetLoadedNotes();

                if (!limiter || !_parityData.Any() || initParity)
                {
                    var redNotes = _noteGridContainer.MapObjects.Where(o => o.Color == ColorRed).OrderBy(o => o.JsonTime).ToList();
                    var blueNotes = _noteGridContainer.MapObjects.Where(o => o.Color == ColorBlue).OrderBy(o => o.JsonTime).ToList();
                    var combined = redNotes.Concat(blueNotes).OrderBy(o => o.JsonTime).ToList();

                    // Remove data for notes that no longer exist
                    _parityData.RemoveAll(d => !combined.Any(n => n.JsonTime == d.JsonTime && n.Color == d.Color && n.PosX == d.PosX && n.PosY == d.PosY));

                    // Clear non-manually tagged data
                    _parityData.RemoveAll(d => !d.ManuallyTagged);

                    // Reconstruct parity data
                    bool forehand = false;
                    ReconstructForNotes(redNotes, ref forehand);
                    forehand = false;
                    ReconstructForNotes(blueNotes, ref forehand);

                    _parityData = _parityData.OrderBy(d => d.JsonTime).ToList();

                    initParity = false;
                }

                // Apply colors
                UpdateLoadedNotes();
                lastEventTime = Time.time;
            }
        }

        private void ReconstructForNotes(List<BaseNote> notes, ref bool forehand)
        {
            BaseNote? prev = null;

            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                var data = _parityData.FirstOrDefault(d => d.JsonTime == n.JsonTime && d.Color == n.Color && d.PosX == n.PosX && d.PosY == n.PosY);
                if (data != null)
                {
                    forehand = data.IsForehand;
                    prev = n;
                    continue;
                }

                if (prev != null)
                {
                    // I'm lazy, potential multi-notes swing
                    if (n.jsonTime - prev.jsonTime <= 0.125 && (n.CutDirection == 8 ||
                        Helper.IsSameDir(Helper.DirectionToDegree[prev.CutDirection] + prev.AngleOffset, Helper.DirectionToDegree[n.CutDirection] + n.AngleOffset)))
                    {
                        // Same parity as before
                        _parityData.Add(new ParityData(n.JsonTime, n.Color, n.PosX, n.PosY, forehand));
                        prev = n;
                        continue;
                    }
                }

                forehand = !forehand;
                _parityData.Add(new ParityData(n.JsonTime, n.Color, n.PosX, n.PosY, forehand));

                prev = n;
            }
        }

        private void UpdateLoadedNotes()
        {
            if (_noteGridContainer == null) return;

            foreach (BaseNote note in _noteGridContainer.LoadedContainers.Keys)
            {
                var data = _parityData.FirstOrDefault(d => d.JsonTime == note.JsonTime && d.Color == note.color && d.PosX == note.PosX && d.PosY == note.PosY);
                if (data == null)
                {
                    continue;
                }

                var visual = _noteGridContainer!.LoadedContainers[note];
                Color color = visual.MaterialPropertyBlock.GetColor(_colorId);
                _noteColors.TryAdd(note, color);
                Color.RGBToHSV(color, out float h, out float s, out float v);

                if (!data.IsForehand)
                {
                    v = Mathf.Clamp01(v - HsvAdjust);
                    visual.MaterialPropertyBlock.SetColor(_colorId, Color.HSVToRGB(h, s, v));
                }

                visual.UpdateMaterials();
            }
        }

        private void ResetLoadedNotes()
        {
            if (_noteGridContainer == null) return;

            foreach (BaseNote note in _noteGridContainer.LoadedContainers.Keys)
            {
                var visual = _noteGridContainer!.LoadedContainers[note];
                if (_noteColors.TryGetValue(note, out Color c))
                {
                    visual.MaterialPropertyBlock.SetColor(_colorId, c);
                    visual.UpdateMaterials();
                }
            }
        }

        private void OnBeatmapActionCreated(object _) => UpdateParity();
        private void OnBeatmapActionUndo(object _) => UpdateParity();
        private void OnBeatmapActionRedo(object _) => UpdateParity();
        private void OnTimeChanged() => UpdateParity(true);
        private void OnFlagPerformed(InputAction.CallbackContext ctx) => FlagParityEvent();
        private void OnUnflagPerformed(InputAction.CallbackContext ctx) => UnflagParityEvent();

    }
}
