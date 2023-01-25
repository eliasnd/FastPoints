using UnityEngine;
using System.Collections.Concurrent;

namespace FastPoints
{
    class DebugMessage
    {
        public enum MessageType { INFO, WARN, ERROR }
        public string msg;
        public MessageType type;

        public DebugMessage(string msg, MessageType type)
        {
            this.msg = msg;
            this.type = type;
        }
    }
    // Allows debugging from multiple threads
    public static class ConcurrentDebug
    {
        static ConcurrentQueue<DebugMessage> logQueue;

        // Call from any thread
        public static void Log(string msg)
        {
            logQueue.Enqueue(new DebugMessage(msg, DebugMessage.MessageType.INFO));
        }

        // Call from any thread
        public static void LogWarning(string msg)
        {
            logQueue.Enqueue(new DebugMessage(msg, DebugMessage.MessageType.WARN));
        }

        // Call from any thread
        public static void LogError(string msg)
        {
            logQueue.Enqueue(new DebugMessage(msg, DebugMessage.MessageType.ERROR));
        }

        // Only call from main thread!
        public static void LogEnqueued(int count = -1)
        {
            if (count == -1)
            {
                DebugMessage dMsg;
                logQueue.TryDequeue(out dMsg);
                while (dMsg != null)
                {
                    switch (dMsg.type)
                    {
                        case DebugMessage.MessageType.INFO:
                            Debug.Log(dMsg.msg);
                            break;
                        case DebugMessage.MessageType.WARN:
                            Debug.LogWarning(dMsg.msg);
                            break;
                        case DebugMessage.MessageType.ERROR:
                            Debug.LogError(dMsg.msg);
                            break;
                        default:
                            break;
                    }

                    logQueue.TryDequeue(out dMsg);
                }
            }
            else
            {
                DebugMessage dMsg;
                logQueue.TryDequeue(out dMsg);

                for (int i = 0; i < count; i++)
                {
                    if (dMsg == null)
                        return;

                    switch (dMsg.type)
                    {
                        case DebugMessage.MessageType.INFO:
                            Debug.Log(dMsg.msg);
                            break;
                        case DebugMessage.MessageType.WARN:
                            Debug.LogWarning(dMsg.msg);
                            break;
                        case DebugMessage.MessageType.ERROR:
                            Debug.LogError(dMsg.msg);
                            break;
                        default:
                            break;
                    }

                    logQueue.TryDequeue(out dMsg);
                }
            }
        }
    }
}