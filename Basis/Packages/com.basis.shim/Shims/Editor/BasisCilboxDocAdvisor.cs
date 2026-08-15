#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace Basis.Shims.Editor
{
    /// <summary>
    /// Teaches the Basis API Reference about the sandbox.
    ///
    /// <para>The reference lives in com.basis.framework.editor and has no idea Cilbox exists; this
    /// registers itself into its provider hook at editor load, so the reference gains a Cilbox
    /// section only in projects that actually have the shims. Every verdict comes from
    /// <see cref="BasisCilboxPermissionModel"/>, which calls the sandboxes' own check methods —
    /// a member cannot read as allowed here and be refused at load.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class BasisCilboxDocAdvisor
    {
        static BasisCilboxDocAdvisor()
        {
            BasisDocCilbox.Provider = Describe;
        }

        private static BasisDocCilboxAdvice Describe(MemberInfo member)
        {
            Type declaring = member?.DeclaringType;
            if (declaring == null || string.IsNullOrEmpty(declaring.FullName)) return null;

            IReadOnlyList<CilboxBoxInfo> boxes = BasisCilboxPermissionModel.Boxes;
            if (boxes == null || boxes.Count == 0) return null;

            var advice = new BasisDocCilboxAdvice();
            var swaps = new List<string>();

            foreach (CilboxBoxInfo box in boxes)
            {
                Type effective = EffectiveType(box, declaring, out bool swapped);
                if (swapped)
                {
                    swaps.Add(BasisCilboxLoc.Get("sdk.cilbox.doc.swap",
                        declaring.Name, effective.Name, BasisCilboxLoc.Get(box.LocalizationKey)));
                }

                advice.Boxes.Add(Judge(box, member, effective, swapped));
            }

            if (swaps.Count > 0) advice.SwapNote = string.Join(" ", swaps);

            AddNotes(advice, member);
            AddRelatedEntry(advice, declaring);

            string typeName = declaring.FullName;
            advice.Reveal = () => BasisCilboxPermissionsWindow.OpenForType(typeName);

            return advice;
        }

        // ------------------------------------------------------------------ verdicts

        /// <summary>
        /// The type the sandbox really hands the script. A cilboxed script writes
        /// <c>UnityEngine.Debug</c> and gets <c>BasisDebugPropsShim</c>; the whitelist and the
        /// member lookup both run against the substitute, so the reference has to follow it.
        /// </summary>
        private static Type EffectiveType(CilboxBoxInfo box, Type declaring, out bool swapped)
        {
            swapped = false;
            if (box?.Probe == null) return declaring;

            try
            {
                if (box.Probe.GetTypeOverride(declaring.FullName, out Type replacement) && replacement != null)
                {
                    swapped = replacement != declaring;
                    return replacement;
                }
            }
            catch (Exception)
            {
                // A probe with no interpreter state behind it; fall back to the written type.
            }
            return declaring;
        }

        private static BasisDocCilboxBoxVerdict Judge(CilboxBoxInfo box, MemberInfo member, Type effective, bool swapped)
        {
            var verdict = new BasisDocCilboxBoxVerdict { BoxName = BasisCilboxLoc.Get(box.LocalizationKey) };

            if (box.Probe == null)
            {
                verdict.Reason = BasisCilboxLoc.Get("sdk.cilbox.doc.unavailable");
                return verdict;
            }

            // TypePassesRecursive, not the raw name check: it strips the by-ref marker, the array
            // suffix and the arity tick the same way the interpreter's own gate does.
            if (!BasisCilboxPermissionModel.TypePassesRecursive(box, effective))
            {
                verdict.Reason = BasisCilboxLoc.Get("sdk.cilbox.doc.typeBlocked", effective.FullName);
                return verdict;
            }

            MemberInfo target = swapped ? MatchOn(effective, member) : member;
            if (target == null)
            {
                verdict.Reason = BasisCilboxLoc.Get("sdk.cilbox.doc.swapMissing", effective.Name, member.Name);
                return verdict;
            }

            switch (target)
            {
                case FieldInfo field:
                {
                    CilboxVerdict result = BasisCilboxPermissionModel.EvaluateField(box, effective, field);
                    verdict.Allowed = result.IsAllowed;
                    verdict.Reason = result.Reason;
                    return verdict;
                }

                case PropertyInfo property:
                    return Accessors(box, effective, verdict, property.GetMethod, property.SetMethod,
                        "sdk.cilbox.doc.readOnly", "sdk.cilbox.doc.writeOnly");

                case EventInfo evt:
                    return Accessors(box, effective, verdict, evt.AddMethod, evt.RemoveMethod,
                        "sdk.cilbox.doc.subscribeOnly", "sdk.cilbox.doc.unsubscribeOnly");

                case MethodInfo method:
                {
                    CilboxVerdict result = BasisCilboxPermissionModel.EvaluateMethod(box, effective, method);
                    verdict.Allowed = result.IsAllowed;
                    verdict.Reason = result.Reason;
                    return verdict;
                }
            }

            verdict.Reason = BasisCilboxLoc.Get("sdk.cilbox.reason.unknown");
            return verdict;
        }

        /// <summary>
        /// Properties and events reach the sandbox as their accessor methods, and the two halves are
        /// whitelisted separately — <c>get_intensity</c> can be listed while <c>set_intensity</c> is
        /// not. Saying "allowed" for a property whose setter is refused is the failure this avoids.
        /// </summary>
        private static BasisDocCilboxBoxVerdict Accessors(CilboxBoxInfo box, Type declaring, BasisDocCilboxBoxVerdict verdict,
                                                          MethodInfo first, MethodInfo second, string firstOnlyKey, string secondOnlyKey)
        {
            CilboxVerdict a = first != null
                ? BasisCilboxPermissionModel.EvaluateMethod(box, declaring, first)
                : new CilboxVerdict(CilboxAccess.Blocked, null);
            CilboxVerdict b = second != null
                ? BasisCilboxPermissionModel.EvaluateMethod(box, declaring, second)
                : new CilboxVerdict(CilboxAccess.Blocked, null);

            bool hasFirst = first != null && a.IsAllowed;
            bool hasSecond = second != null && b.IsAllowed;

            verdict.Allowed = hasFirst || hasSecond;

            if (hasFirst && hasSecond) verdict.Reason = a.Reason;
            else if (hasFirst) verdict.Reason = BasisCilboxLoc.Get(firstOnlyKey, b.Reason ?? string.Empty);
            else if (hasSecond) verdict.Reason = BasisCilboxLoc.Get(secondOnlyKey, a.Reason ?? string.Empty);
            else verdict.Reason = a.Reason ?? b.Reason ?? BasisCilboxLoc.Get("sdk.cilbox.reason.methodBlocked");

            return verdict;
        }

        /// <summary>The same member by name on the substituted type, or null when it has none.</summary>
        private static MemberInfo MatchOn(Type replacement, MemberInfo member)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            MemberInfo[] found;
            try { found = replacement.GetMember(member.Name, Flags); }
            catch (Exception) { return null; }

            if (found == null || found.Length == 0) return null;

            foreach (MemberInfo candidate in found)
            {
                if (candidate.MemberType == member.MemberType) return candidate;
            }
            return found[0];
        }

        // ------------------------------------------------------------------ prose

        /// <summary>
        /// Caveats that belong to this member rather than to cilbox in general — the general ones
        /// live in the Permissions window's notes, and repeating them under every member would bury
        /// the two that actually change what you write here.
        /// </summary>
        private static void AddNotes(BasisDocCilboxAdvice advice, MemberInfo member)
        {
            if (member is EventInfo)
                advice.Notes.Add(BasisCilboxLoc.Get("sdk.cilbox.note.delegateUnsubscribe.body"));

            if (member is FieldInfo { IsLiteral: true })
                advice.Notes.Add(BasisCilboxLoc.Get("sdk.cilbox.reason.constInlined"));

            if (member is MethodInfo && member.Name.StartsWith("GetComponent", StringComparison.Ordinal))
                advice.Notes.Add(BasisCilboxLoc.Get("sdk.cilbox.note.getComponentSpelling.body"));
        }

        /// <summary>
        /// Points at the hand-written catalog entry covering this type, when there is one. Those
        /// examples carry context a generated snippet cannot — consent gates, ownership, pooling.
        /// </summary>
        private static void AddRelatedEntry(BasisDocCilboxAdvice advice, Type declaring)
        {
            string typeName = declaring.FullName;

            foreach (CilboxApiEntry entry in BasisCilboxApiCatalog.Entries)
            {
                foreach (CilboxApiRequirement requirement in entry.Requires)
                {
                    if (!string.Equals(requirement.TypeName, typeName, StringComparison.Ordinal)) continue;

                    advice.RelatedTitle = BasisCilboxLoc.Get(entry.TitleKey);
                    advice.RelatedExample = entry.Example;
                    return;
                }
            }
        }
    }
}
#endif
