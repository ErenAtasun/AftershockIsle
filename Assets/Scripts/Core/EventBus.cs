using System;
using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Core
{
    /// <summary>
    /// Sistemlerin birbirini doğrudan tanımadan haberleşmesi için tip tabanlı olay yolu.
    /// Olaylar küçük struct'lardır; abone olan OnDisable'da aboneliği bırakmalıdır.
    /// </summary>
    public static class EventBus
    {
        static readonly Dictionary<Type, Delegate> handlers = new Dictionary<Type, Delegate>();

        public static void Subscribe<T>(Action<T> handler)
        {
            handlers[typeof(T)] = handlers.TryGetValue(typeof(T), out var existing)
                ? Delegate.Combine(existing, handler)
                : handler;
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            if (!handlers.TryGetValue(typeof(T), out var existing)) return;

            var remaining = Delegate.Remove(existing, handler);
            if (remaining == null) handlers.Remove(typeof(T));
            else handlers[typeof(T)] = remaining;
        }

        public static void Publish<T>(T evt)
        {
            if (handlers.TryGetValue(typeof(T), out var handler))
                ((Action<T>)handler).Invoke(evt);
        }

        // Domain reload kapalıyken önceki oturumdan kalan aboneleri temizler.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => handlers.Clear();
    }
}
