using System;
using System.Threading;

namespace BijiSDK.Unity
{
    /// <summary>Dispatches callbacks to the Unity thread captured during construction.</summary>
    public sealed class UnityMainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly SynchronizationContext _context;
        private readonly int _threadId;

        /// <summary>Create this dispatcher from Awake or Start on Unity's main thread.</summary>
        public UnityMainThreadDispatcher()
        {
            _context = SynchronizationContext.Current;
            if (_context == null ||
                _context.GetType().FullName != "UnityEngine.UnitySynchronizationContext")
            {
                throw new InvalidOperationException(
                    "Create UnityMainThreadDispatcher on Unity's main thread, from Awake or Start.");
            }

            _threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public void Post(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (Thread.CurrentThread.ManagedThreadId == _threadId)
            {
                action();
                return;
            }

            _context.Post(state => ((Action)state)(), action);
        }
    }
}
