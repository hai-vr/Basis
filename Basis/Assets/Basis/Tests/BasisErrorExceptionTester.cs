using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Tests
{
    /// <summary>
    /// Manual QA component that emits log errors and exceptions on demand so the error
    /// pipeline (BasisExceptionNotifier dialogues + BasisErrorReportSender crash reports)
    /// can be exercised. Trigger from the Inspector context menu, the on-screen buttons,
    /// or runFullSuiteOnStart. Logged exceptions are reported but keep execution running;
    /// the "Throw" actions raise genuine uncaught exceptions with real CLR stack traces.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Basis/Tests/Error & Exception Tester")]
    public class BasisErrorExceptionTester : MonoBehaviour
    {
        [Header("On-Screen Controls")]
        public bool showOnScreenButtons = true;

        [Header("Behaviour")]
        public bool runFullSuiteOnStart = false;
        public bool uniquePerRun = true;
        public float throwStagger = 0.25f;

        private static int _nonce;

        private void Start()
        {
            if (runFullSuiteOnStart) RunFullSuite();
        }

        [ContextMenu("Run Full Suite")]
        public void RunFullSuite()
        {
            LogErrors();
            LogExceptions();
            ThrowAllUncaught();
        }

        [ContextMenu("Log Errors")]
        public void LogErrors()
        {
            Debug.LogError(Tag("Plain Unity error via Debug.LogError"));
            Debug.LogErrorFormat("Formatted Unity error code={0} state={1}", 42, "broken");
            Debug.LogError(Tag("Error carrying a context object"), this);

            BasisDebug.LogError(Tag("System error"), BasisDebug.LogTag.System);
            BasisDebug.LogError(Tag("Networking error"), BasisDebug.LogTag.Networking);
            BasisDebug.LogError(Tag("IK error"), BasisDebug.LogTag.IK);
            BasisDebug.LogError(Tag("Avatar error"), BasisDebug.LogTag.Avatar);
            BasisDebug.LogError(Tag("Voice error"), BasisDebug.LogTag.Voice);
            BasisDebug.LogError(new InvalidOperationException(Tag("Exception routed through BasisDebug.LogError")), BasisDebug.LogTag.Core);
        }

        [ContextMenu("Log Exceptions (all types)")]
        public void LogExceptions()
        {
            foreach (Exception e in BuildExceptions())
            {
                Debug.LogException(e, this);
            }
        }

        private IEnumerable<Exception> BuildExceptions()
        {
            yield return new NullReferenceException(Tag("Object reference not set"));
            yield return new ArgumentNullException("avatar", Tag("Avatar argument was null"));
            yield return new ArgumentException(Tag("Value was not acceptable"), "mode");
            yield return new ArgumentOutOfRangeException("index", 17, Tag("Index outside collection bounds"));
            yield return new IndexOutOfRangeException(Tag("Array index out of range"));
            yield return new InvalidOperationException(Tag("Operation not valid in current state"));
            yield return new InvalidCastException(Tag("Cannot cast component to target type"));
            yield return new NotImplementedException(Tag("Pathway not implemented yet"));
            yield return new NotSupportedException(Tag("Operation not supported on this platform"));
            yield return new DivideByZeroException(Tag("Attempted divide by zero"));
            yield return new FormatException(Tag("Input string was not in a correct format"));
            yield return new OverflowException(Tag("Arithmetic operation overflowed"));
            yield return new TimeoutException(Tag("Operation timed out"));
            yield return new KeyNotFoundException(Tag("Key was not present in the dictionary"));
            yield return new OutOfMemoryException(Tag("Insufficient memory (simulated)"));
            yield return new BasisTestException(Tag("Custom Basis exception type"));
            yield return new BasisTestException(Tag("Wrapper exception"), new InvalidOperationException("Underlying cause"));
            yield return new AggregateException(Tag("Multiple failures"),
                new TimeoutException("First failure"),
                new IndexOutOfRangeException("Second failure"));
        }

        [ContextMenu("Throw All (uncaught, staggered)")]
        public void ThrowAllUncaught()
        {
            string[] throwers =
            {
                nameof(ThrowNullReference),
                nameof(ThrowIndexOutOfRange),
                nameof(ThrowDivideByZero),
                nameof(ThrowInvalidCast),
                nameof(ThrowOverflow),
                nameof(ThrowArgument),
                nameof(ThrowInvalidOperation),
                nameof(ThrowCustom),
            };
            for (int i = 0; i < throwers.Length; i++)
            {
                Invoke(throwers[i], Mathf.Max(0f, throwStagger) * i);
            }
        }

        private void ThrowNullReference()
        {
            object o = null;
            _ = o.ToString();
        }

        private void ThrowIndexOutOfRange()
        {
            int[] a = new int[1];
            _ = a[5];
        }

        private void ThrowDivideByZero()
        {
            int[] zero = new int[1];
            _ = 100 / zero[0];
        }

        private void ThrowInvalidCast()
        {
            object boxed = 7;
            _ = (string)boxed;
        }

        private void ThrowOverflow()
        {
            checked
            {
                int max = int.MaxValue;
                _ = max + 1;
            }
        }

        private void ThrowArgument()
        {
            throw new ArgumentException("Genuinely thrown argument exception", "value");
        }

        private void ThrowInvalidOperation()
        {
            Queue<int> empty = new();
            _ = empty.Dequeue();
        }

        private void ThrowCustom()
        {
            throw new BasisTestException("Genuinely thrown custom exception", new NullReferenceException("inner null"));
        }

        private string Tag(string message)
        {
            return uniquePerRun ? $"{message} #{++_nonce}" : message;
        }

        private void OnGUI()
        {
            if (!showOnScreenButtons) return;

            GUILayout.BeginArea(new Rect(10, 10, 260, Screen.height - 20));
            GUILayout.BeginVertical("box");
            GUILayout.Label("Basis Error / Exception Tester");

            if (GUILayout.Button("Run Full Suite")) RunFullSuite();
            if (GUILayout.Button("Log Errors")) LogErrors();
            if (GUILayout.Button("Log Exceptions (all types)")) LogExceptions();
            if (GUILayout.Button("Throw All (uncaught)")) ThrowAllUncaught();

            GUILayout.Space(6);
            GUILayout.Label("Throw one (uncaught):");
            if (GUILayout.Button("NullReference")) Invoke(nameof(ThrowNullReference), 0f);
            if (GUILayout.Button("IndexOutOfRange")) Invoke(nameof(ThrowIndexOutOfRange), 0f);
            if (GUILayout.Button("DivideByZero")) Invoke(nameof(ThrowDivideByZero), 0f);
            if (GUILayout.Button("InvalidCast")) Invoke(nameof(ThrowInvalidCast), 0f);
            if (GUILayout.Button("Overflow")) Invoke(nameof(ThrowOverflow), 0f);
            if (GUILayout.Button("Argument")) Invoke(nameof(ThrowArgument), 0f);
            if (GUILayout.Button("InvalidOperation")) Invoke(nameof(ThrowInvalidOperation), 0f);
            if (GUILayout.Button("Custom (BasisTestException)")) Invoke(nameof(ThrowCustom), 0f);

            GUILayout.Space(6);
            uniquePerRun = GUILayout.Toggle(uniquePerRun, "Unique messages per run");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }

    public sealed class BasisTestException : Exception
    {
        public BasisTestException(string message) : base(message) { }
        public BasisTestException(string message, Exception inner) : base(message, inner) { }
    }
}
