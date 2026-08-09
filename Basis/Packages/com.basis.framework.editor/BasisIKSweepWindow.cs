using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    /// <summary>
    /// One sweep's controls, hosted as a page inside <see cref="BasisIKSweepWindow"/>.
    ///
    /// Every sweep used to be its own EditorWindow with its own menu entry — three dozen entries under
    /// Basis ▸ Debug ▸ IK that were the same window with different knobs. They are pages now: the host
    /// owns the chrome (title, description, scrolling, output path) and each page only draws its own
    /// configuration and results. Pages are discovered by <see cref="TypeCache"/>, so adding one is
    /// still just adding a file.
    /// </summary>
    public abstract class BasisIKSweepPage
    {
        /// <summary>Sidebar section this page is filed under.</summary>
        public abstract string Group { get; }

        /// <summary>Sidebar entry and page heading.</summary>
        public abstract string Title { get; }

        /// <summary>One paragraph on what the sweep proves. Shown under the heading.</summary>
        public virtual string Description => null;

        /// <summary>Where this page sits inside its group; ties break alphabetically.</summary>
        public virtual int Order => 100;

        /// <summary>The window hosting this page — use it to request a repaint.</summary>
        public EditorWindow Host { get; internal set; }

        /// <summary>Called once, the first time the page is shown.</summary>
        public virtual void OnEnable() { }

        /// <summary>Called when the host window closes, for pages that hooked editor events.</summary>
        public virtual void OnDisable() { }

        /// <summary>Called on the editor's slow tick while this page is the visible one.</summary>
        public virtual void OnInspectorUpdate() { }

        /// <summary>Draws the page body. The host has already drawn the heading and opened a scroll view.</summary>
        public abstract void Draw();
    }

    /// <summary>
    /// The single home for every IK sweep, gate and recorder.
    ///
    /// Left is a searchable index grouped by body area; right is the selected sweep. Replaces the
    /// ~40 individual Basis ▸ Debug ▸ IK menu entries with one, and gives every sweep the same
    /// Basis chrome instead of each rolling its own.
    /// </summary>
    public class BasisIKSweepWindow : EditorWindow
    {
        private const string SelectionKey = "Basis.IKSweeps.Selected";
        private const float SidebarWidth = 226f;

        private static readonly string[] GroupOrder =
        {
            "Run All",
            "Arm & Elbow",
            "Leg & Foot",
            "Spine & Torso",
            "Tracking & Calibration",
            "Eyes & Filters",
            "Recorders",
        };

        private List<BasisIKSweepPage> _pages;
        private readonly HashSet<BasisIKSweepPage> _enabled = new HashSet<BasisIKSweepPage>();
        private BasisIKSweepPage _selected;
        private string _search = string.Empty;
        private Vector2 _indexScroll;
        private Vector2 _bodyScroll;

        private static GUIStyle _selectedRow;
        private static GUIStyle _idleRow;
        private static bool _rowStylesAreLight;

        private static void InvalidateRowStylesOnSkinChange()
        {
            if (_selectedRow != null && _rowStylesAreLight == BasisEditorUI.Light) return;
            _rowStylesAreLight = BasisEditorUI.Light;
            _selectedRow = null;
            _idleRow = null;
        }

        private static GUIStyle SelectedRow
        {
            get
            {
                InvalidateRowStylesOnSkinChange();
                if (_selectedRow == null)
                {
                    _selectedRow = new GUIStyle(EditorStyles.label) { padding = new RectOffset(10, 4, 0, 0) };
                    _selectedRow.normal.textColor = BasisEditorUI.Light ? new Color(0.08f, 0.08f, 0.08f) : Color.white;
                }
                return _selectedRow;
            }
        }

        private static GUIStyle IdleRow
        {
            get
            {
                InvalidateRowStylesOnSkinChange();
                if (_idleRow == null)
                {
                    _idleRow = new GUIStyle(EditorStyles.label) { padding = new RectOffset(10, 4, 0, 0) };
                    _idleRow.normal.textColor = BasisEditorUI.Muted;
                }
                return _idleRow;
            }
        }

        [MenuItem("Basis/Debug/IK Sweeps", false, 600)]
        public static void ShowWindow()
        {
            BasisIKSweepWindow w = GetWindow<BasisIKSweepWindow>();
            w.titleContent = new GUIContent("IK Sweeps");
            w.minSize = new Vector2(760, 520);
            w.Show();
        }

        private void OnEnable() => Rebuild();

        private void OnDisable()
        {
            foreach (BasisIKSweepPage page in _enabled)
            {
                try { page.OnDisable(); }
                catch (Exception e) { Debug.LogError($"[IK Sweeps] {page.Title} failed to shut down: {e}"); }
            }
            _enabled.Clear();
        }

        private void OnInspectorUpdate()
        {
            _selected?.OnInspectorUpdate();
        }

        private void Rebuild()
        {
            _pages = new List<BasisIKSweepPage>();
            foreach (Type t in TypeCache.GetTypesDerivedFrom<BasisIKSweepPage>())
            {
                if (t.IsAbstract || t.GetConstructor(Type.EmptyTypes) == null) continue;
                try
                {
                    var page = (BasisIKSweepPage)Activator.CreateInstance(t);
                    page.Host = this;
                    _pages.Add(page);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[IK Sweeps] Could not create page {t.Name}: {e.Message}");
                }
            }

            _pages = _pages
                .OrderBy(p => GroupRank(p.Group))
                .ThenBy(p => p.Order)
                .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string wanted = EditorPrefs.GetString(SelectionKey, string.Empty);
            _selected = _pages.FirstOrDefault(p => p.Title == wanted) ?? _pages.FirstOrDefault();
            Activate(_selected);
        }

        private static int GroupRank(string group)
        {
            int i = Array.IndexOf(GroupOrder, group);
            return i < 0 ? GroupOrder.Length : i;
        }

        private void Activate(BasisIKSweepPage page)
        {
            if (page == null) return;
            if (_enabled.Add(page))
            {
                try { page.OnEnable(); }
                catch (Exception e) { Debug.LogError($"[IK Sweeps] {page.Title} failed to initialise: {e}"); }
            }
            _selected = page;
            EditorPrefs.SetString(SelectionKey, page.Title);
        }

        private void OnGUI()
        {
            if (_pages == null) Rebuild();

            EditorGUILayout.BeginHorizontal();
            DrawIndex();
            DrawBody();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawIndex()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));
            Rect side = new Rect(0f, 0f, SidebarWidth, position.height);
            BasisEditorUI.Fill(side, BasisEditorUI.Light ? new Color(0f, 0f, 0f, 0.09f) : new Color(0f, 0f, 0f, 0.28f), 0f);

            GUILayout.Space(6f);
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            GUILayout.Space(4f);

            _indexScroll = EditorGUILayout.BeginScrollView(_indexScroll);
            string group = null;
            bool any = false;
            foreach (BasisIKSweepPage page in _pages)
            {
                if (!Matches(page)) continue;
                any = true;

                if (page.Group != group)
                {
                    group = page.Group;
                    GUILayout.Space(4f);
                    BasisEditorUI.SectionTitle(group);
                }

                bool active = ReferenceEquals(page, _selected);
                Rect r = GUILayoutUtility.GetRect(new GUIContent(page.Title), EditorStyles.label, GUILayout.Height(19f));
                if (active) BasisEditorUI.Fill(r, BasisEditorUI.Light ? new Color(0f, 0f, 0f, 0.09f) : new Color(1f, 1f, 1f, 0.07f), 3f);
                if (active) BasisEditorUI.Fill(new Rect(r.x, r.y + 2f, 2f, r.height - 4f), BasisEditorUI.Accent, 1f);

                if (GUI.Button(r, page.Title, active ? SelectedRow : IdleRow)) Activate(page);
            }

            if (!any) BasisEditorUI.Note("  No sweep matches that search.");
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private bool Matches(BasisIKSweepPage page)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return page.Title.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || page.Group.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || (page.Description != null && page.Description.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawBody()
        {
            EditorGUILayout.BeginVertical();
            if (_selected == null)
            {
                BasisEditorUI.Help("No IK sweeps found in this project.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            _bodyScroll = BasisEditorUI.BeginPage(_bodyScroll);
            BasisEditorUI.Header(_selected.Title, _selected.Description);

            try
            {
                _selected.Draw();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception e)
            {
                BasisEditorUI.Help($"{_selected.Title} threw while drawing:\n{e}", MessageType.Error);
            }

            BasisEditorUI.EndPage();
            EditorGUILayout.EndVertical();
        }
    }
}
