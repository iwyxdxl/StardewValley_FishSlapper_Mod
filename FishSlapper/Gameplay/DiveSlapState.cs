namespace FishSlapper.Gameplay
{
    internal enum DiveSlapState
    {
        None,
        Windup,
        Diving,
        Slapping,
        ResolveSuccessPauseBefore,
        ResolveSuccess,
        ResolveFailPauseBefore,
        ResolveFail,
        ResolveFailPauseAfter,
        Returning
    }
}
