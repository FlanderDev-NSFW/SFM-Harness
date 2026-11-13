namespace Harness.Logic
{
    /// <summary>
    /// Finds a piston's pushes in from its stroke, frame by frame. Stroke 0 is all the way in, 1 all the way out. A push runs from where the piston
    /// turned inward to where it stops going in: it turns out again, reaches the end, or rests. Its travel is the share of the full stroke it covered.
    /// A small dead band keeps tremor in the drive from counting as pushes.
    /// </summary>
    public sealed class PushCounter
    {
        public const float DeadBand = 0.01f, RestSeconds = 0.2f;
        private float _last = float.NaN, _from, _still; private bool _goingIn;

        public void Reset() { _last = float.NaN; _goingIn = false; _still = 0f; }

        /// <summary>Returns the travel of a push that ended this frame, or 0.</summary>
        public float Update(float stroke, float dt)
        {
            if (float.IsNaN(_last)) { _last = stroke; _from = stroke; return 0f; }
            float travel = 0f;
            if (_goingIn)
            {
                if (stroke < _last) { _last = stroke; _still = 0f; }
                else _still += dt;
                bool turned = stroke > _last + DeadBand, atEnd = _last <= 0.001f, rested = _still >= RestSeconds;
                if (turned || atEnd || rested)
                {
                    travel = _from - _last; _goingIn = false;
                    _from = stroke; _last = stroke; _still = 0f;
                }
            }
            else
            {
                if (stroke > _from) _from = stroke;          // the furthest out since the last push: where the next one starts
                if (stroke < _from - DeadBand) { _goingIn = true; _last = stroke; _still = 0f; }
                else _last = stroke;
            }
            return travel;
        }
    }
}
