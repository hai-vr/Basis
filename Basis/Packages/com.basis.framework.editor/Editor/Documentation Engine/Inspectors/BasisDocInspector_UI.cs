// Editor/BasisDocInspector_UI.cs
// Universal, DB-aware inspector: shows API Reference for any MonoBehaviour that
// has docs in BasisDocDB; otherwise falls back to default inspector.
// Also filters out Unity/engine members so you only see your API.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(MonoBehaviour), true, isFallback = true)]
public class BasisDocInspector_UI : Editor
{
    // Path your generator writes to
    private const string DbAssetPath = "Packages/com.basis.framework.editor/Editor/Documentation Engine/BasisDocDB.asset";

    // Data
    private BasisDocDB _db;
    private List<MemberRow> _all = new();
    private List<MemberRow> _view = new();

    // UI
    private ToolbarSearchField _search;
    private ToolbarToggle _fltFields, _fltProps, _fltMethods, _fltEvents, _fltInherited;
    private ListView _list;
    private ScrollView _detail;

    // If we decide this type shouldn't use the custom panel, we fall back to default
    private bool _useApiPanel;

    // ---------- Theme ----------
    private static readonly Color ColBorder = new(0, 0, 0, 0.12f);
    private static readonly Color ColMuted = new(1f, 1f, 1f, 0.75f);
    private static readonly Color ColCard = new(0.1f, 0.1f, 0.1f, 0.06f);
    private static readonly Color ColChipBg = new(0.2f, 0.2f, 0.2f, 0.25f);
    private static readonly Color ColChipOn = new(0.18f, 0.5f, 0.9f, 0.25f);

    private const string MonoFont = "Lucida Console, Consolas, Courier New, monospace";

    // ---------- Row model ----------
    private class MemberRow
    {
        public MemberInfo Info;
        public string Kind;       // "Fields" | "Properties" | "Methods" | "Events"
        public string Name;
        public string TypeName;   // field/property type or method return type
        public string Signature;  // pretty method signature
        public string Display;    // one-line label for list

        // docs from DB
        public string Summary;
        public string Remarks;
        public string Returns;
        public string ValueDoc;
        public string Since;
        public string ObsoleteMsg;
        public string[] Platforms = Array.Empty<string>();

        public string[] ParamNames = Array.Empty<string>();
        public string[] ParamDocs = Array.Empty<string>();

        public string[] TypeParamNames = Array.Empty<string>();
        public string[] TypeParamDocs = Array.Empty<string>();

        public (string cref, string doc)[] Exceptions = Array.Empty<(string, string)>();
        public string[] See = Array.Empty<string>();
        public string[] SeeAlso = Array.Empty<string>();

        public List<string> Examples = new();

        public bool IsInherited(Type host) => Info?.DeclaringType != host;
    }

    // ---------- Inspector entry ----------
    public override VisualElement CreateInspectorGUI()
    {
        // Load DB once here
        _db = AssetDatabase.LoadAssetAtPath<BasisDocDB>(DbAssetPath);

        var hostType = target.GetType();
        _useApiPanel = ShouldHandleType(hostType);

        if (!_useApiPanel)
        {
            return new IMGUIContainer(() => base.OnInspectorGUI());
        }

        var root = new VisualElement
        {
            style =
            {
                marginLeft = 6, marginRight = 6, marginTop = 6, marginBottom = 6
            }
        };

        var defaultIMGUI = new IMGUIContainer(() => base.OnInspectorGUI());
        root.Add(defaultIMGUI);

        // Spacing + divider
        root.Add(Spacer(10));
        root.Add(Divider());
        root.Add(Spacer(6));

        // ====== API Reference (Foldout) ======
        var apiFoldout = new Foldout
        {
            text = "Basis API Reference",
            value = false // closed by default
        };

        // Add your API content inside the foldout
        var api = BuildApiSplitView();
        apiFoldout.Add(api);

        root.Add(apiFoldout);

        return root;
    }

    private bool ShouldHandleType(Type t)
    {
        if (_db == null) return false;
        if (!typeof(MonoBehaviour).IsAssignableFrom(t)) return false;

        // Only if it's "ours" (same assembly OR allowed namespaces)
        if (!IsOurs(t, t)) return false;

        // Quick probe: any docs for this type?
        foreach (var mi in ReflectMembersForProbe(t))
        {
            if (DbHasDocsFor(mi))
                return true;
        }
        return false;
    }

    // Lighter pass used only to decide if we have any docs at all
    private IEnumerable<MemberInfo> ReflectMembersForProbe(Type t)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        for (var cur = t; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
        {
            foreach (var f in cur.GetFields(flags)) yield return f;
            foreach (var p in cur.GetProperties(flags)) yield return p;
            foreach (var e in cur.GetEvents(flags)) yield return e;
            foreach (var m in cur.GetMethods(flags)) if (!m.IsSpecialName) yield return m;
        }
    }

    private bool DbHasDocsFor(MemberInfo mi)
    {
        var kind = mi switch
        {
            FieldInfo => "Field",
            PropertyInfo => "Property",
            MethodInfo => "Method",
            EventInfo => "Event",
            _ => null
        };
        if (kind == null) return false;

        var typeFull = mi.DeclaringType?.FullName;
        var paramCount = (mi as MethodInfo)?.GetParameters().Length ?? 0;
        var hit = _db.FindFor(typeFull, mi.Name, kind, paramCount);
        return hit != null;
    }

    // ---------- Build API UI (three panes) ----------
    private VisualElement BuildApiSplitView()
    {
        // OUTER: [ Left(Filters) | Right(InnerSplit) ]
        var outer = new TwoPaneSplitView(0, 220, TwoPaneSplitViewOrientation.Horizontal)
        {
            style = { minHeight = 420, height = 460 }
        };

        // LEFT: filters only
        var left = new VisualElement { style = { flexDirection = FlexDirection.Column } };
        left.style.overflow = Overflow.Hidden;

        var filtersHeader = new Toolbar();
        filtersHeader.style.position = Position.Relative; // stacking context
        filtersHeader.Add(new Label("Filter")
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                marginLeft = 6, marginRight = 6
            }
        });
        left.Add(filtersHeader);

        // Filter chips
        var chips = new Toolbar();
        chips.style.position = Position.Relative;
        _fltFields = Chip("Fields", true);
        _fltProps = Chip("Properties", true);
        _fltMethods = Chip("Methods", true);
        _fltEvents = Chip("Events", true);
        _fltInherited = Chip("Inherited", true);
        chips.Add(_fltFields);
        chips.Add(_fltProps);
        chips.Add(_fltMethods);
        chips.Add(_fltEvents);
        chips.Add(new ToolbarSpacer());
        chips.Add(_fltInherited);
        left.Add(ChipLegend()); // small legend for tag colors
        left.Add(chips);

        outer.Add(left);

        // RIGHT of OUTER: an inner split that holds [ Middle(List) | Right(Details) ]
        var inner = new TwoPaneSplitView(0, 340, TwoPaneSplitViewOrientation.Horizontal);
        outer.Add(inner);

        // MIDDLE: search + list
        var middle = new VisualElement { style = { flexDirection = FlexDirection.Column, flexGrow = 1 } };
        middle.style.overflow = Overflow.Hidden;

        var searchBar = new Toolbar();
        searchBar.style.position = Position.Relative;
        _search = new ToolbarSearchField { style = { flexGrow = 1 } };
        _search.RegisterValueChangedCallback(_ => ApplyFilter());
        searchBar.Add(_search);
        middle.Add(searchBar);

        _list = new ListView
        {
            virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
            selectionType = SelectionType.Single,
            style =
            {
                flexGrow = 1,
                overflow = Overflow.Hidden,
                borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor = ColBorder, borderBottomColor = ColBorder, borderLeftColor = ColBorder, borderRightColor = ColBorder
            }
        };
        _list.makeItem = () =>
        {
            var row = new VisualElement
            {
                style =
                {
                    paddingLeft = 8, paddingRight = 8, paddingTop = 6, paddingBottom = 6
                }
            };
            var title = new Label { name = "title", style = { unityFontStyleAndWeight = FontStyle.Bold } };
            var sub = new Label { name = "sub", style = { color = ColMuted, fontSize = 11, whiteSpace = WhiteSpace.Normal } };
            row.Add(title);
            row.Add(sub);
            return row;
        };
        _list.bindItem = (ve, i) =>
        {
            var row = _view[i];
            ve.Q<Label>("title").text = row.Display;
            ve.Q<Label>("sub").text = row.Summary;
        };
        _list.onSelectionChange += _ => ShowDetails(_list.selectedIndex);
        middle.Add(_list);

        // Clip the ListView internal viewport once it’s mounted
        _list.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            var viewport = _list.Q<VisualElement>("unity-content-viewport");
            if (viewport != null)
                viewport.style.overflow = Overflow.Hidden;
        });

        inner.Add(middle);

        // RIGHT: details
        _detail = new ScrollView { style = { paddingLeft = 8, paddingRight = 8 } };
        _detail.style.overflow = Overflow.Hidden;
        _detail.style.position = Position.Relative;
        inner.Add(_detail);

        // Build data now
        BuildData();
        ApplyFilter();

        return outer;
    }

    private VisualElement ChipLegend()
    {
        var row = new VisualElement { style = { marginLeft = 6, marginRight = 6, marginTop = 4, marginBottom = 2 } };
        var hint = new Label("Tags: ")
        {
            style = { color = ColMuted, fontSize = 10, marginBottom = 2 }
        };
        row.Add(hint);
        return row;
    }

    private ToolbarToggle Chip(string text, bool value)
    {
        var t = new ToolbarToggle { text = text, value = value };
        t.RegisterValueChangedCallback(_ => ApplyFilter());
        t.style.unityTextAlign = TextAnchor.MiddleCenter;
        t.style.backgroundColor = value ? ColChipOn : ColChipBg;
        t.RegisterCallback<ChangeEvent<bool>>(e => { t.style.backgroundColor = e.newValue ? ColChipOn : ColChipBg; });
        return t;
    }

    // ---------- Data build & filter ----------
    private void BuildData()
    {
        _all.Clear();
        var host = target.GetType();

        foreach (var mi in ReflectMembers(host))
            _all.Add(ToRow(mi, host));

        _view = _all;
        _list.itemsSource = _view;
    }

    private IEnumerable<MemberInfo> ReflectMembers(Type t)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        for (var cur = t; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
        {
            foreach (var f in cur.GetFields(flags))
            {
                if (f.IsSpecialName) continue;
                if (Attribute.IsDefined(f, typeof(HideInInspector))) continue;
                if (!ShouldIncludeMember(t, f)) continue;
                yield return f;
            }
            foreach (var p in cur.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (p.GetMethod == null && p.SetMethod == null) continue;
                if (!ShouldIncludeMember(t, p)) continue;
                yield return p;
            }
            foreach (var e in cur.GetEvents(flags))
            {
                if (!ShouldIncludeMember(t, e)) continue;
                yield return e;
            }

            foreach (var m in cur.GetMethods(flags))
            {
                if (m.IsSpecialName) continue;
                if (!ShouldIncludeMember(t, m)) continue;
                yield return m;
            }
        }
    }

    private MemberRow ToRow(MemberInfo mi, Type hostType)
    {
        var row = new MemberRow
        {
            Info = mi,
            Kind = mi switch
            {
                FieldInfo => "Fields",
                PropertyInfo => "Properties",
                MethodInfo => "Methods",
                EventInfo => "Events",
                _ => "Other"
            },
            Name = mi.Name
        };

        if (mi is FieldInfo fi)
        {
            row.TypeName = NiceType(fi.FieldType);
            row.Display = $"Field • {row.TypeName}  {row.Name}";
        }
        else if (mi is PropertyInfo pi)
        {
            row.TypeName = NiceType(pi.PropertyType);
            row.Display = $"Property • {row.TypeName}  {row.Name}";
        }
        else if (mi is MethodInfo mm)
        {
            row.TypeName = NiceType(mm.ReturnType);
            row.Signature = BuildSignature(mm);
            row.Display = $"Method • {row.Signature}";
        }
        else if (mi is EventInfo ei)
        {
            row.Display = $"Event • {ei.EventHandlerType?.Name}  {row.Name}";
        }

        // Docs from DB
        if (_db != null)
        {
            var kindSingle = row.Kind.TrimEnd('s'); // Fields -> Field
            var typeFull = mi.DeclaringType?.FullName;
            var paramCount = (mi as MethodInfo)?.GetParameters().Length ?? 0;

            var hit = _db.FindFor(typeFull, mi.Name, kindSingle, paramCount);
            if (hit != null)
            {
                row.Summary = NullIfEmpty(hit.Summary);
                row.Remarks = NullIfEmpty(hit.Remarks);
                row.Returns = NullIfEmpty(hit.Returns);
                row.ValueDoc = NullIfEmpty(hit.Value);
                row.Since = NullIfEmpty(hit.Since);
                row.ObsoleteMsg = NullIfEmpty(hit.ObsoleteMsg);
                row.Platforms = hit.Platforms?.ToArray() ?? Array.Empty<string>();

                row.ParamNames = hit.ParamNames?.ToArray() ?? Array.Empty<string>();
                row.ParamDocs = hit.ParamDocs?.ToArray() ?? Array.Empty<string>();

                row.TypeParamNames = hit.TypeParamNames?.ToArray() ?? Array.Empty<string>();
                row.TypeParamDocs = hit.TypeParamDocs?.ToArray() ?? Array.Empty<string>();

                // pair exceptions
                if (hit.ExceptionCrefs != null && hit.ExceptionDocs != null)
                {
                    var n = Math.Min(hit.ExceptionCrefs.Count, hit.ExceptionDocs.Count);
                    var list = new List<(string, string)>();
                    for (int i = 0; i < n; i++)
                        list.Add((hit.ExceptionCrefs[i], hit.ExceptionDocs[i]));
                    row.Exceptions = list.ToArray();
                }

                row.See = hit.SeeCrefs?.ToArray() ?? Array.Empty<string>();
                row.SeeAlso = hit.SeeAlsoCrefs?.ToArray() ?? Array.Empty<string>();

                if (hit.Examples != null && hit.Examples.Count > 0)
                {
                    row.Examples = new List<string>(hit.Examples);
                }
            }
        }

        // Fallback: Tooltip for fields
        if (string.IsNullOrEmpty(row.Summary) && mi is FieldInfo f2)
        {
            var tt = f2.GetCustomAttribute<TooltipAttribute>();
            if (tt != null) row.Summary = tt.tooltip;
        }

        if (row.IsInherited(hostType))
            row.Display += "    (inherited)";

        return row;
    }

    private void ApplyFilter()
    {
        var host = target.GetType();
        var q = _search?.value ?? "";

        bool showFields = _fltFields?.value ?? true;
        bool showProps = _fltProps?.value ?? true;
        bool showMethods = _fltMethods?.value ?? true;
        bool showEvents = _fltEvents?.value ?? true;
        bool showInherited = _fltInherited?.value ?? true;

        _view = _all.Where(r =>
        {
            if (!showInherited && r.IsInherited(host)) return false;
            if (!showFields && r.Kind == "Fields") return false;
            if (!showProps && r.Kind == "Properties") return false;
            if (!showMethods && r.Kind == "Methods") return false;
            if (!showEvents && r.Kind == "Events") return false;

            if (string.IsNullOrWhiteSpace(q)) return true;
            return (r.Display?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (r.Summary?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        })
        .OrderBy(r => r.Kind)
        .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

        _list.itemsSource = _view;
        _list.Rebuild();

        if (_view.Count > 0)
            _list.selectedIndex = 0;
        else
            _detail?.Clear();
    }

    // ---------- Detail panel ----------
    private void ShowDetails(int index)
    {
        _detail.Clear();
        if (index < 0 || index >= _view.Count) return;
        var d = _view[index];

        // Title + tags
        var titleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        titleRow.Add(Title(d.Name));
        titleRow.Add(Spacer(6));
        titleRow.Add(ChipTag(d.Kind.TrimEnd('s')));

        if (!string.IsNullOrEmpty(d.ObsoleteMsg))
            titleRow.Add(ChipTag("Obsolete", new Color(0.9f, 0.4f, 0.3f, 0.4f)));

        if (!string.IsNullOrEmpty(d.Since))
            titleRow.Add(ChipTag($"Since {d.Since}", new Color(0.3f, 0.8f, 0.5f, 0.35f)));

        if (d.Platforms is { Length: > 0 })
        {
            foreach (var p in d.Platforms) titleRow.Add(ChipTag(p));
        }

        _detail.Add(titleRow);

        if (!string.IsNullOrEmpty(d.Signature))
            _detail.Add(Subtle($"Signature: {d.Signature}"));
        else if (!string.IsNullOrEmpty(d.TypeName))
            _detail.Add(Subtle($"Type: {d.TypeName}"));

        if (!string.IsNullOrEmpty(d.Summary))
            _detail.Add(CardBlock("Summary", d.Summary));

        if (!string.IsNullOrEmpty(d.Remarks))
            _detail.Add(CardBlock("Remarks", d.Remarks));

        if (d.Info is MethodInfo mm)
        {
            if (d.TypeParamNames.Length > 0)
                _detail.Add(ListBlock("Type Parameters", d.TypeParamNames, d.TypeParamDocs));

            if (d.ParamNames.Length > 0)
                _detail.Add(ListBlock("Parameters", d.ParamNames, d.ParamDocs));

            if (!string.IsNullOrEmpty(d.Returns) && d.TypeName != "void")
                _detail.Add(CardBlock("Returns", d.Returns));
        }
        else if (d.Info is PropertyInfo)
        {
            if (!string.IsNullOrEmpty(d.ValueDoc))
                _detail.Add(CardBlock("Value", d.ValueDoc));
        }

        if (d.Exceptions.Length > 0)
        {
            var terms = d.Exceptions.Select(e => (e.cref, e.doc)).ToArray();
            _detail.Add(ExceptionBlock("Exceptions", terms));
        }

        if (d.See.Length > 0 || d.SeeAlso.Length > 0)
        {
            var links = new List<string>();
            if (d.See.Length > 0) links.AddRange(d.See);
            if (d.SeeAlso.Length > 0) links.AddRange(d.SeeAlso.Select(s => s + " (see also)"));
            _detail.Add(BulletBlock("Related", links));
        }

        // Examples: show each with colorized preview + copyable plaintext
        if (d.Examples.Count > 0)
        {
            for (int i = 0; i < d.Examples.Count; i++)
            {
                var label = d.Examples.Count == 1 ? "Example" : $"Example {i + 1}";
                _detail.Add(ColorizedCodeBlock(label, d.Examples[i]));
            }
        }

        // auto usage snippet
        var snippet = GenerateSnippet(d, (Component)target);
        if (!string.IsNullOrEmpty(snippet))
            _detail.Add(ColorizedCodeBlock("How to call", snippet, showCopyButton: true));

        // live value for fields/props
        if (d.Info is FieldInfo fi)
        {
            if (TryValue(() => fi.GetValue(target), out var val))
                _detail.Add(CardBlock("Current Value", val));
        }
        else if (d.Info is PropertyInfo pi && pi.CanRead)
        {
            if (TryValue(() => pi.GetValue(target, null), out var val))
                _detail.Add(CardBlock("Current Value", val));
        }

        // invoke button for parameterless methods
        if (d.Info is MethodInfo m && m.GetParameters().Length == 0)
        {
            var btn = new Button(() =>
            {
                try { m.Invoke(target, null); }
                catch (Exception ex) { Debug.LogException(ex); }
            })
            { text = Application.isPlaying ? "Invoke" : "Invoke (enter Play Mode)" };
            btn.SetEnabled(Application.isPlaying);
            _detail.Add(btn);
        }
    }

    // ---------- Filtering helpers: keep our code, drop Unity/enginey stuff ----------
    private static readonly HashSet<string> NameBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Methods
        "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren",
        "GetComponentInParent", "GetComponentsInParent",

        // Properties/fields commonly inherited from Unity types (legacy shorthands included)
        "transform", "gameObject", "tag", "name", "hideFlags",
        "renderer", "particleSystem", "rigidbody", "rigidbody2D",
        "camera", "light", "animation", "constantForce", "collider",
        "collider2D", "HingeJoint", "networkView"
    };

    private static bool IsUnityFramework(MemberInfo mi)
    {
        var dt = mi.DeclaringType;
        if (dt == null) return false;

        var ns = dt.Namespace ?? "";
        if (ns.StartsWith("UnityEngine", StringComparison.Ordinal)) return true;
        if (ns.StartsWith("UnityEditor", StringComparison.Ordinal)) return true;

        return dt == typeof(MonoBehaviour)
            || dt == typeof(Component)
            || dt == typeof(Behaviour)
            || dt == typeof(GameObject)
            || dt == typeof(UnityEngine.Object);
    }

    private static bool IsBlockedByName(MemberInfo mi)
    {
        var n = mi.Name;
        if (NameBlocklist.Contains(n)) return true;
        if (n.StartsWith("GetComponent", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool IsOurs(Type hostType, MemberInfo miOrType)
    {
        var hostAsm = hostType.Assembly;
        var declType = miOrType as MemberInfo != null ? ((MemberInfo)miOrType).DeclaringType : (Type)miOrType;
        declType ??= hostType;
        var declAsm = declType.Assembly;
        if (declAsm != null && declAsm == hostAsm) return true;

        var ns = declType.Namespace ?? "";
        if (ns.StartsWith("Basis", StringComparison.Ordinal)) return true;

        return false;
    }

    private static bool ShouldIncludeMember(Type hostType, MemberInfo mi)
    {
        if (IsUnityFramework(mi)) return false;
        if (IsBlockedByName(mi)) return false;
        if (!IsOurs(hostType, mi)) return false;
        return true;
    }

    // ---------- Small UI helpers ----------
    private static VisualElement Divider() => new VisualElement
    {
        style =
        {
            height = 1,
            backgroundColor = new Color(0,0,0,0.2f)
        }
    };

    private static VisualElement Spacer(float px) => new VisualElement { style = { height = px } };

    private static Label Title(string text) => new Label(text)
    {
        style =
        {
            unityFontStyleAndWeight = FontStyle.Bold,
            fontSize = 13,
            marginTop = 6, marginBottom = 2
        }
    };

    private static Label Subtle(string text) => new Label(text)
    {
        style =
        {
            color = ColMuted,
            fontSize = 11,
            marginBottom = 4
        }
    };

    private static Label BlockHeader(string text) => new Label(text)
    {
        style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 }
    };

    private VisualElement CardBlock(string title, string body)
    {
        var inner = new VisualElement();
        inner.Add(BlockHeader(title));
        var lbl = new Label(body) { style = { whiteSpace = WhiteSpace.Normal } };
        inner.Add(lbl);
        Card(inner);
        return inner;
    }

    private VisualElement BulletBlock(string title, IEnumerable<string> items)
    {
        var inner = new VisualElement();
        inner.Add(BlockHeader(title));
        foreach (var it in items)
            inner.Add(new Label("• " + it) { style = { whiteSpace = WhiteSpace.Normal } });
        Card(inner);
        return inner;
    }

    private VisualElement ListBlock(string title, string[] names, string[] docs)
    {
        var inner = new VisualElement();
        inner.Add(BlockHeader(title));
        for (int i = 0; i < names.Length; i++)
        {
            var doc = (i < docs.Length) ? docs[i] : "";
            inner.Add(new Label($"• {names[i]} — {doc}") { style = { whiteSpace = WhiteSpace.Normal } });
        }
        Card(inner);
        return inner;
    }

    private VisualElement ExceptionBlock(string title, (string cref, string doc)[] items)
    {
        var inner = new VisualElement();
        inner.Add(BlockHeader(title));
        foreach (var (cref, doc) in items)
        {
            var line = string.IsNullOrEmpty(cref) ? $"• {doc}" : $"• {cref} — {doc}";
            inner.Add(new Label(line) { style = { whiteSpace = WhiteSpace.Normal } });
        }
        Card(inner);
        return inner;
    }

    private VisualElement ColorizedCodeBlock(string title, string code, bool showCopyButton = true)
    {
        var wrap = new VisualElement();
        wrap.Add(BlockHeader(title));

        // Copyable plain text
        var tf = new TextField { multiline = true, value = code };
        tf.isReadOnly = true;
        tf.style.whiteSpace = WhiteSpace.Normal;
        tf.style.unityTextAlign = TextAnchor.UpperLeft;
        tf.style.marginTop = 4;
        tf.style.height = Mathf.Clamp(40 + code.Length / 2, 60, 260);
        wrap.Add(tf);

        if (showCopyButton)
        {
            wrap.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = code) { text = "Copy code" });
        }

        Card(wrap);
        return wrap;
    }

    private VisualElement ChipTag(string text, Color? c = null)
    {
        var tag = new Label(text)
        {
            style =
            {
                backgroundColor = c ?? ColChipBg,
                unityTextAlign = TextAnchor.MiddleCenter,
                paddingLeft = 6, paddingRight = 6, paddingTop = 2, paddingBottom = 2,
                marginLeft = 4, marginRight = 0,
                borderTopLeftRadius = 999, borderTopRightRadius = 999,
                borderBottomLeftRadius = 999, borderBottomRightRadius = 999,
                fontSize = 10
            }
        };
        return tag;
    }

    private void Card(VisualElement content)
    {
        var card = new VisualElement
        {
            style =
            {
                marginTop = 6, marginBottom = 8,
                paddingLeft = 8, paddingRight = 8, paddingTop = 6, paddingBottom = 6,
                backgroundColor = ColCard,
                borderTopLeftRadius = 8, borderTopRightRadius = 8,
                borderBottomLeftRadius = 8, borderBottomRightRadius = 8
            }
        };
        card.Add(content);
        _detail.Add(card);
    }

    private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private string NiceType(Type t)
    {
        if (t == null) return "void";
        if (t == typeof(void)) return "void";
        if (!t.IsGenericType) return t.Name;
        var root = t.Name.Split('`')[0];
        var args = string.Join(", ", t.GetGenericArguments().Select(NiceType));
        return $"{root}<{args}>";
    }

    private string BuildSignature(MethodInfo m)
    {
        var ps = m.GetParameters();
        var parms = string.Join(", ", ps.Select(p =>
        {
            var mod = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0 ? "params " : "";
            var t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
            return $"{mod}{NiceType(t)} {p.Name}";
        }));
        return $"{NiceType(m.ReturnType)} {m.Name}({parms})";
    }

    private string GenerateSnippet(MemberRow d, Component comp)
    {
        var compType = comp.GetType().Name;
        var varName = char.ToLowerInvariant(compType[0]) + compType.Substring(1);
        var sb = new StringBuilder();

        switch (d.Kind)
        {
            case "Fields":
                {
                    var isStatic = (d.Info as FieldInfo)?.IsStatic ?? false;
                    if (isStatic)
                    {
                        sb.AppendLine("// read");
                        sb.AppendLine($"var value = {d.Info.DeclaringType.Name}.{d.Name};");
                        sb.AppendLine();
                        sb.AppendLine("// write");
                        sb.AppendLine($"{d.Info.DeclaringType.Name}.{d.Name} = /* new {d.TypeName} */;");
                    }
                    else
                    {
                        sb.AppendLine($"{compType} {varName} = GetComponent<{compType}>();");
                        sb.AppendLine($"var value = {varName}.{d.Name};");
                        sb.AppendLine($"{varName}.{d.Name} = /* new {d.TypeName} */;");
                    }
                    break;
                }
            case "Properties":
                {
                    var canSet = (d.Info as PropertyInfo)?.SetMethod != null;
                    sb.AppendLine($"{compType} {varName} = GetComponent<{compType}>();");
                    sb.AppendLine($"var value = {varName}.{d.Name};");
                    if (canSet) sb.AppendLine($"{varName}.{d.Name} = /* new {d.TypeName} */;");
                    break;
                }
            case "Methods":
                {
                    var mm = (MethodInfo)d.Info;
                    var ps = mm.GetParameters();
                    sb.AppendLine($"{compType} {varName} = GetComponent<{compType}>();");
                    sb.Append($"{varName}.{mm.Name}(");
                    sb.Append(string.Join(", ", ps.Select(p => $"/* {p.Name}: {NiceType(p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType)} */")));
                    sb.AppendLine(");");
                    break;
                }
            case "Events":
                {
                    sb.AppendLine($"{compType} {varName} = GetComponent<{compType}>();");
                    sb.AppendLine($"{varName}.{d.Name} += MyHandler;");
                    sb.AppendLine("// ... later");
                    sb.AppendLine($"{varName}.{d.Name} -= MyHandler;");
                    sb.AppendLine();
                    sb.AppendLine("void MyHandler() { /* ... */ }");
                    break;
                }
        }
        return sb.ToString();
    }

    private bool TryValue(Func<object> getter, out string text)
    {
        try
        {
            var v = getter();
            text = v switch
            {
                null => "null",
                string s => $"\"{s}\"",
                UnityEngine.Object uo => $"{uo.name} ({uo.GetType().Name})",
                _ => v.ToString()
            };
            return true;
        }
        catch
        {
            text = null;
            return false;
        }
    }

    // ---------- Lightweight C# colorizer ----------
    // Converts plain code to Unity rich text (for preview labels).
    // We keep it simple: keywords, types, strings, comments, numbers.
    private static readonly string[] CSharpKeywords =
    {
        "public","private","protected","internal","static","readonly","const","new","override","virtual","abstract","sealed",
        "class","struct","interface","enum","delegate","event",
        "void","int","float","double","bool","string","char","byte","long","short","decimal","var",
        "in","out","ref","params","return","if","else","switch","case","default","break","continue","for","foreach","while","do",
        "try","catch","finally","throw","using","namespace","get","set","add","remove","this","base","null","true","false"
    };

    private static readonly Regex RxString = new("\"(?:\\\\.|[^\"])*\"");
    private static readonly Regex RxChar = new("'(?:\\\\.|[^'])'");
    private static readonly Regex RxLineC = new("//.*?$", RegexOptions.Multiline);
    private static readonly Regex RxBlockC = new(@"/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex RxNumber = new(@"\b\d+(\.\d+)?([fFdDlL])?\b");
    private static readonly Regex RxIdent = new(@"\b[_A-Za-z]\w*\b");

    private static string Wrap(string s, string hex, bool bold = false)
        => bold ? $"<b><color={hex}>{s}</color></b>" : $"<color={hex}>{s}</color>";
}
