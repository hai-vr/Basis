using System;
using System.Collections.Generic;
using System.Reflection;
using Basis.Editor.Localization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class BasisSDKCommonInspector
{
    // Source of truth for preset labels lives in BasisContentTagPresets so the client
    // filter UI surfaces the same options creators see in this inspector.
    private static string[] ContentTagPresets => BasisContentTagPresets.All;

    public static Button DocumentationButton(VisualElement rootElement, string Text)
    {
        Button fixMeButton = new Button();
        fixMeButton.text = Text;

        Color backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        fixMeButton.style.backgroundColor = new StyleColor(backgroundColor);
        fixMeButton.style.color = new StyleColor(Color.white);
        fixMeButton.style.fontSize = 14;
        fixMeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        fixMeButton.style.paddingTop = 6;
        fixMeButton.style.paddingBottom = 6;
        fixMeButton.style.paddingLeft = 12;
        fixMeButton.style.paddingRight = 12;
        fixMeButton.style.marginBottom = 10;
        fixMeButton.style.borderTopLeftRadius = 8;
        fixMeButton.style.borderTopRightRadius = 8;
        fixMeButton.style.borderBottomLeftRadius = 8;
        fixMeButton.style.borderBottomRightRadius = 8;
        fixMeButton.style.borderLeftWidth = 0;
        fixMeButton.style.borderRightWidth = 0;
        fixMeButton.style.borderTopWidth = 0;
        fixMeButton.style.borderBottomWidth = 3;
        fixMeButton.style.unityTextAlign = TextAnchor.MiddleCenter;
        fixMeButton.style.alignSelf = Align.Auto;

        fixMeButton.RegisterCallback<MouseEnterEvent>(evt =>
        {
            fixMeButton.style.backgroundColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f, 1f));
        });
        fixMeButton.RegisterCallback<MouseLeaveEvent>(evt =>
        {
            fixMeButton.style.backgroundColor = new StyleColor(backgroundColor);
        });

        rootElement.Add(fixMeButton);
        return fixMeButton;
    }

    public static void CreateContentTagsFoldout(VisualElement parent, BasisContentBase content)
    {
        if (content == null) return;
        if (content.BasisBundleDescription == null)
        {
            content.BasisBundleDescription = new BasisBundleDescription();
        }
        if (content.BasisBundleDescription.Tags == null)
        {
            content.BasisBundleDescription.Tags = new string[0];
        }

        Foldout foldout = new Foldout
        {
            text = BasisEditorLocalization.Get("sdk.commonInspector.contentTags.foldout"),
            value = false,
        };
        parent.Add(foldout);

        Label help = new Label(BasisEditorLocalization.Get("sdk.commonInspector.contentTags.help"))
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal,
                marginBottom = 6,
                opacity = 0.85f,
            },
        };
        foldout.Add(help);

        Label presetLabel = new Label(BasisEditorLocalization.Get("sdk.commonInspector.contentTags.presets")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } };
        foldout.Add(presetLabel);

        for (int i = 0; i < ContentTagPresets.Length; i++)
        {
            string preset = ContentTagPresets[i];
            Toggle toggle = new Toggle(preset)
            {
                value = ContainsTag(content.BasisBundleDescription.Tags, preset),
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(content, "Toggle Content Tag");
                if (evt.newValue)
                {
                    content.BasisBundleDescription.Tags = AppendIfMissing(content.BasisBundleDescription.Tags, preset);
                }
                else
                {
                    content.BasisBundleDescription.Tags = RemoveTag(content.BasisBundleDescription.Tags, preset);
                }
                MarkContentDirty(content);
            });
            foldout.Add(toggle);
        }

        Label customLabel = new Label(BasisEditorLocalization.Get("sdk.commonInspector.contentTags.custom")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } };
        foldout.Add(customLabel);

        VisualElement addRow = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, marginBottom = 4 },
        };
        TextField customField = new TextField
        {
            style = { flexGrow = 1, marginRight = 4 },
        };
        customField.textEdition.placeholder = BasisEditorLocalization.Get("sdk.commonInspector.contentTags.placeholder");
        Button addButton = new Button { text = BasisEditorLocalization.Get("sdk.commonInspector.contentTags.add") };

        VisualElement chipsRow = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap },
        };

        Action rebuildChips = null;
        rebuildChips = () =>
        {
            chipsRow.Clear();
            string[] tags = content?.BasisBundleDescription?.Tags;
            if (tags == null) return;
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i];
                if (IsPreset(tag)) continue; // presets render as toggles above
                VisualElement chip = BuildTagChip(tag, () =>
                {
                    Undo.RecordObject(content, "Remove Content Tag");
                    content.BasisBundleDescription.Tags = RemoveTag(content.BasisBundleDescription.Tags, tag);
                    MarkContentDirty(content);
                    rebuildChips();
                });
                chipsRow.Add(chip);
            }
        };

        Action commitCustomTag = () =>
        {
            string raw = customField.value;
            if (string.IsNullOrWhiteSpace(raw)) return;
            string trimmed = raw.Trim();
            Undo.RecordObject(content, "Add Content Tag");
            content.BasisBundleDescription.Tags = AppendIfMissing(content.BasisBundleDescription.Tags, trimmed);
            MarkContentDirty(content);
            customField.SetValueWithoutNotify(string.Empty);
            rebuildChips();
        };

        addButton.clicked += () => commitCustomTag();
        customField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                commitCustomTag();
                evt.StopPropagation();
            }
        });

        addRow.Add(customField);
        addRow.Add(addButton);
        foldout.Add(addRow);
        foldout.Add(chipsRow);
        rebuildChips();
    }

    private static VisualElement BuildTagChip(string label, Action onRemove)
    {
        VisualElement chip = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                marginRight = 4,
                marginBottom = 4,
                paddingLeft = 6,
                paddingRight = 2,
                paddingTop = 2,
                paddingBottom = 2,
                backgroundColor = new StyleColor(new Color(0.27f, 0.31f, 0.40f, 1f)),
                borderTopLeftRadius = 8,
                borderTopRightRadius = 8,
                borderBottomLeftRadius = 8,
                borderBottomRightRadius = 8,
            },
        };
        Label text = new Label(label) { style = { color = Color.white, marginRight = 4 } };
        Button remove = new Button(() => onRemove?.Invoke())
        {
            text = "x",
            style =
            {
                width = 18,
                height = 18,
                paddingLeft = 0,
                paddingRight = 0,
                paddingTop = 0,
                paddingBottom = 0,
                marginLeft = 0,
                marginRight = 0,
                marginTop = 0,
                marginBottom = 0,
                fontSize = 10,
            },
        };
        chip.Add(text);
        chip.Add(remove);
        return chip;
    }

    private static bool ContainsTag(string[] tags, string candidate)
    {
        if (tags == null) return false;
        for (int i = 0; i < tags.Length; i++)
        {
            if (string.Equals(tags[i], candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string[] AppendIfMissing(string[] tags, string tag)
    {
        if (tags == null)
        {
            return new[] { tag };
        }
        if (ContainsTag(tags, tag))
        {
            return tags;
        }
        var next = new string[tags.Length + 1];
        Array.Copy(tags, next, tags.Length);
        next[tags.Length] = tag;
        return next;
    }

    private static string[] RemoveTag(string[] tags, string tag)
    {
        if (tags == null || tags.Length == 0) return tags ?? new string[0];
        int kept = 0;
        for (int i = 0; i < tags.Length; i++)
        {
            if (!string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase)) kept++;
        }
        if (kept == tags.Length) return tags;
        var next = new string[kept];
        int j = 0;
        for (int i = 0; i < tags.Length; i++)
        {
            if (!string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
            {
                next[j++] = tags[i];
            }
        }
        return next;
    }

    private static bool IsPreset(string tag)
    {
        for (int i = 0; i < ContentTagPresets.Length; i++)
        {
            if (string.Equals(ContentTagPresets[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void MarkContentDirty(BasisContentBase content)
    {
        EditorUtility.SetDirty(content);
        if (PrefabUtility.IsPartOfPrefabInstance(content))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(content);
        }
    }

    /// <summary>
    /// Builds the "Spawn Placement" section for a prop: how the prop asks to arrive in the world.
    /// Rows that do not apply to the chosen placement are hidden rather than disabled, so the
    /// section only ever shows knobs that actually do something.
    /// </summary>
    public static void CreatePropSpawnFoldout(VisualElement parent, BasisProp prop)
    {
        if (prop == null) return;

        Foldout foldout = new Foldout
        {
            text = BasisEditorLocalization.Get("sdk.propInspector.spawn.foldout"),
            value = false,
        };
        parent.Add(foldout);

        Label help = new Label(BasisEditorLocalization.Get("sdk.propInspector.spawn.help"))
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal,
                marginBottom = 6,
                opacity = 0.85f,
            },
        };
        foldout.Add(help);

        EnumField placementField = new EnumField(BasisEditorLocalization.Get("sdk.propInspector.spawn.placement"), prop.SpawnMetaData.Placement);
        foldout.Add(placementField);

        Label placementHelp = new Label
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal,
                marginBottom = 6,
                marginLeft = 4,
                opacity = 0.7f,
            },
        };
        foldout.Add(placementHelp);

        EnumField handField = new EnumField(BasisEditorLocalization.Get("sdk.propInspector.spawn.hand"), prop.SpawnMetaData.Hand);
        foldout.Add(handField);

        Toggle distanceToggle = new Toggle(BasisEditorLocalization.Get("sdk.propInspector.spawn.overrideDistance"))
        {
            value = prop.SpawnMetaData.HasCustomDistance,
        };
        foldout.Add(distanceToggle);

        FloatField distanceField = new FloatField(BasisEditorLocalization.Get("sdk.propInspector.spawn.distance"))
        {
            value = prop.SpawnMetaData.HasCustomDistance ? prop.SpawnMetaData.Distance : BasisPropSpawnMetaData.FallbackDistance,
        };
        foldout.Add(distanceField);

        Toggle alignField = new Toggle(BasisEditorLocalization.Get("sdk.propInspector.spawn.alignToSurface"))
        {
            value = prop.SpawnMetaData.AlignToSurface,
        };
        foldout.Add(alignField);

        Toggle faceField = new Toggle(BasisEditorLocalization.Get("sdk.propInspector.spawn.faceThePlayer"))
        {
            value = prop.SpawnMetaData.FaceThePlayer,
        };
        foldout.Add(faceField);

        Toggle scaleToggle = new Toggle(BasisEditorLocalization.Get("sdk.propInspector.spawn.overrideScale"))
        {
            value = prop.SpawnMetaData.HasCustomScale,
        };
        foldout.Add(scaleToggle);

        FloatField scaleField = new FloatField(BasisEditorLocalization.Get("sdk.propInspector.spawn.uniformScale"))
        {
            value = prop.SpawnMetaData.HasCustomScale && prop.SpawnMetaData.UniformScale > 0f ? prop.SpawnMetaData.UniformScale : 1f,
        };
        foldout.Add(scaleField);

        void RefreshRows()
        {
            BasisPropSpawnPlacement placement = prop.SpawnMetaData.Placement;
            bool inHand = placement == BasisPropSpawnPlacement.InHand;
            bool onGround = placement == BasisPropSpawnPlacement.OnGround;
            bool usesDistance = inHand || onGround || placement == BasisPropSpawnPlacement.InAirAtDistance;
            bool usesFacing = onGround
                || placement == BasisPropSpawnPlacement.InAirAtDistance
                || placement == BasisPropSpawnPlacement.InFrontOfPlayer
                || placement == BasisPropSpawnPlacement.AtPlayerOrigin;

            placementHelp.text = SpawnPlacementHelp(placement);

            SetRowVisible(handField, inHand);
            SetRowVisible(distanceToggle, usesDistance);
            SetRowVisible(distanceField, usesDistance && prop.SpawnMetaData.HasCustomDistance);
            SetRowVisible(alignField, onGround);
            SetRowVisible(faceField, usesFacing);
            SetRowVisible(scaleToggle, true);
            SetRowVisible(scaleField, prop.SpawnMetaData.HasCustomScale);
        }

        void Apply(Action mutate, string undoName)
        {
            Undo.RecordObject(prop, undoName);
            mutate();
            MarkContentDirty(prop);
            RefreshRows();
        }

        placementField.RegisterValueChangedCallback(evt =>
            Apply(() => prop.SpawnMetaData.Placement = (BasisPropSpawnPlacement)evt.newValue, "Change Prop Spawn Placement"));

        handField.RegisterValueChangedCallback(evt =>
            Apply(() => prop.SpawnMetaData.Hand = (BasisPropSpawnHand)evt.newValue, "Change Prop Spawn Hand"));

        distanceToggle.RegisterValueChangedCallback(evt => Apply(() =>
        {
            prop.SpawnMetaData.HasCustomDistance = evt.newValue;
            if (evt.newValue && prop.SpawnMetaData.Distance <= 0f)
            {
                prop.SpawnMetaData.Distance = BasisPropSpawnMetaData.FallbackDistance;
                distanceField.SetValueWithoutNotify(prop.SpawnMetaData.Distance);
            }
        }, "Toggle Prop Spawn Distance Override"));

        distanceField.RegisterValueChangedCallback(evt => Apply(() =>
        {
            float clamped = Mathf.Max(evt.newValue, 0.05f);
            prop.SpawnMetaData.Distance = clamped;
            if (!Mathf.Approximately(clamped, evt.newValue))
            {
                distanceField.SetValueWithoutNotify(clamped);
            }
        }, "Change Prop Spawn Distance"));

        alignField.RegisterValueChangedCallback(evt =>
            Apply(() => prop.SpawnMetaData.AlignToSurface = evt.newValue, "Toggle Prop Align To Surface"));

        faceField.RegisterValueChangedCallback(evt =>
            Apply(() => prop.SpawnMetaData.FaceThePlayer = evt.newValue, "Toggle Prop Face The Player"));

        scaleToggle.RegisterValueChangedCallback(evt => Apply(() =>
        {
            prop.SpawnMetaData.HasCustomScale = evt.newValue;
            if (evt.newValue && prop.SpawnMetaData.UniformScale <= 0f)
            {
                prop.SpawnMetaData.UniformScale = 1f;
                scaleField.SetValueWithoutNotify(prop.SpawnMetaData.UniformScale);
            }
        }, "Toggle Prop Spawn Scale Override"));

        scaleField.RegisterValueChangedCallback(evt => Apply(() =>
        {
            float clamped = Mathf.Max(evt.newValue, 0.001f);
            prop.SpawnMetaData.UniformScale = clamped;
            if (!Mathf.Approximately(clamped, evt.newValue))
            {
                scaleField.SetValueWithoutNotify(clamped);
            }
        }, "Change Prop Spawn Scale"));

        RefreshRows();
    }

    private static void SetRowVisible(VisualElement row, bool visible)
    {
        row.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static string SpawnPlacementHelp(BasisPropSpawnPlacement placement)
    {
        switch (placement)
        {
            case BasisPropSpawnPlacement.Raycast:
                return BasisEditorLocalization.Get("sdk.propInspector.spawn.placement.raycast");
            case BasisPropSpawnPlacement.InFrontOfPlayer:
                return BasisEditorLocalization.Get("sdk.propInspector.spawn.placement.inFront");
            case BasisPropSpawnPlacement.AtPlayerOrigin:
                return BasisEditorLocalization.Get("sdk.propInspector.spawn.placement.playerOrigin");
            case BasisPropSpawnPlacement.InAirAtDistance:
                return BasisEditorLocalization.Get("sdk.propInspector.spawn.placement.inAir");
            case BasisPropSpawnPlacement.OnGround:
                return BasisEditorLocalization.Get("sdk.propInspector.spawn.placement.onGround");
            case BasisPropSpawnPlacement.InHand:
                return BasisEditorLocalization.Get("sdk.propInspector.spawn.placement.inHand");
            default:
                return BasisEditorLocalization.Get("sdk.propInspector.spawn.placement.unspecified");
        }
    }

    public static void CreateBuildOptionsDropdown(VisualElement parent)
    {
        BasisAssetBundleObject assetBundleObject =
            AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(
                BasisAssetBundleObject.AssetBundleObject);

        Foldout foldout = new Foldout
        {
            text = BasisEditorLocalization.Get("sdk.commonInspector.buildOptions.foldout"),
            value = false
        };
        parent.Add(foldout);

        Toggle toggle = new Toggle(BasisEditorLocalization.Get("sdk.commonInspector.useCustomPassword"))
        {
            value = assetBundleObject.UseCustomPassword
        };

        TextField passwordField = new TextField(BasisEditorLocalization.Get("sdk.commonInspector.password"))
        {
            value = assetBundleObject.UserSelectedPassword,
            isPasswordField = true // masks input
        };

        // Initial state
        passwordField.SetEnabled(toggle.value);
        passwordField.style.display = toggle.value
            ? DisplayStyle.Flex
            : DisplayStyle.None;

        toggle.RegisterValueChangedCallback(evt =>
        {
            assetBundleObject.UseCustomPassword = evt.newValue;

            passwordField.SetEnabled(evt.newValue);
            passwordField.style.display = evt.newValue
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (!evt.newValue)
            {
                assetBundleObject.UserSelectedPassword = "";
                passwordField.value = "";
            }

            EditorUtility.SetDirty(assetBundleObject);
        });

        passwordField.RegisterValueChangedCallback(evt =>
        {
            assetBundleObject.UserSelectedPassword = evt.newValue;
            EditorUtility.SetDirty(assetBundleObject);
        });

        foldout.Add(toggle);
        foldout.Add(passwordField);

        foreach (BuildAssetBundleOptions option in Enum.GetValues(typeof(BuildAssetBundleOptions)))
        {
            if (option == 0)
            {
                continue; // Skip "None"
            }
            // Check if the enum field has the Obsolete attribute
            FieldInfo fieldInfo = typeof(BuildAssetBundleOptions).GetField(option.ToString());
            if (fieldInfo != null && Attribute.IsDefined(fieldInfo, typeof(ObsoleteAttribute)))
            {
                continue; // Skip obsolete options from being shown

            }
            Toggle BABOTggle = new Toggle(option.ToString())
            {
                value = assetBundleObject.BuildAssetBundleOptions.HasFlag(option)
            };

            BABOTggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    assetBundleObject.BuildAssetBundleOptions |= option;
                }
                else
                {
                    assetBundleObject.BuildAssetBundleOptions &= ~option;
                }
            });

            foldout.Add(BABOTggle);
        }
        EditorUtility.SetDirty(assetBundleObject);
        AssetDatabase.SaveAssets();
    }
    public static void CreateBuildTargetOptions(VisualElement parent)
    {
        BasisAssetBundleObject assetBundleObject = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
        // Multi-select dropdown (Foldout with Toggles)
        Foldout buildTargetFoldout = new Foldout { text = BasisEditorLocalization.Get("sdk.commonInspector.buildTargets.foldout"), value = false }; // Expanded by default
        parent.Add(buildTargetFoldout);

        foreach (var target in BasisSDKConstants.allowedTargets)
        {
            // Check if the target is already selected
            bool isSelected = assetBundleObject.selectedTargets.Contains(target);

            Toggle toggle = new Toggle(BasisSDKConstants.targetDisplayNames[target])
            {
                value = isSelected // Set the toggle based on whether the target is in the selected list
            };

            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    assetBundleObject.selectedTargets.Add(target);
                else
                    assetBundleObject.selectedTargets.Remove(target);
            });


            buildTargetFoldout.Add(toggle);
        }
        EditorUtility.SetDirty(assetBundleObject);
        AssetDatabase.SaveAssets();
    }
}
