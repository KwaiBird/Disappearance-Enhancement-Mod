namespace Disappearance.GameplayQoL
{
    internal enum ModalOwner { None, Padlock, Checkpoint }
    internal enum ModalPhase { Idle, Opening, Active, Closing }

    // Coordinates ownership only. Each module retains and restores its own game state.
    internal sealed class ModalSession
    {
        private int nextToken;

        internal ModalOwner Owner { get; private set; }
        internal ModalPhase Phase { get; private set; }
        internal int SceneHandle { get; private set; }
        internal int Token { get; private set; }
        internal bool IsOccupied => Owner != ModalOwner.None;

        internal bool AllowsMenuInput(bool paused, bool pauseToggle, bool transition) =>
            !IsOccupied && !transition && (pauseToggle || paused);

        internal bool TryAcquire(ModalOwner owner, int sceneHandle, out int token)
        {
            token = 0;
            // Unity scene handles are opaque integers; their sign is not a validity test.
            if (owner == ModalOwner.None || IsOccupied) return false;
            if (++nextToken <= 0) nextToken = 1;
            Owner = owner;
            Phase = ModalPhase.Opening;
            SceneHandle = sceneHandle;
            Token = nextToken;
            token = Token;
            return true;
        }

        internal bool Activate(ModalOwner owner, int token)
        {
            if (!Matches(owner, token) || Phase != ModalPhase.Opening) return false;
            Phase = ModalPhase.Active;
            return true;
        }

        internal bool BeginClosing(ModalOwner owner, int token)
        {
            if (!Matches(owner, token) || Phase == ModalPhase.Closing) return false;
            Phase = ModalPhase.Closing;
            return true;
        }

        internal bool Release(ModalOwner owner, int token)
        {
            if (!Matches(owner, token)) return false;
            Owner = ModalOwner.None;
            Phase = ModalPhase.Idle;
            SceneHandle = 0;
            Token = 0;
            return true;
        }

        private bool Matches(ModalOwner owner, int token)
        {
            return owner != ModalOwner.None && Owner == owner && token != 0 && Token == token;
        }
    }
}
