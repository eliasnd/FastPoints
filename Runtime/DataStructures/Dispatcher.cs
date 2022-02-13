using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace FastPoints {
    // Dispatches Actions to main thread for execution
    public class Dispatcher {
        ConcurrentQueue<Action> actions;

        public int Count {
            get { return actions.Count; }
        }

        public Dispatcher() {
            actions = new ConcurrentQueue<Action>();
        }

        public void Enqueue(Action a) {
            actions.Enqueue(a);
        }

        public async Task EnqueueAsync(Action a) {
            await Task.Run(() => {
                bool actionComplete = false;
                actions.Enqueue( () => { a(); actionComplete = true; } );
                while (!actionComplete)
                    Thread.Sleep(50);
                return;
            });
        }

        public bool TryDequeue(out Action a) {
            return actions.TryDequeue(out a);
        }
    }
}