namespace HVR.Vixxy
{
    public interface IHVRVixxyNetworkable
    {
        void RequireNetworked(string address, float defaultValue);
    }

    public enum HVRVixxyNetMessageType
    {
        SubmitFalse = 0,
        SubmitTrue = 1,
        /// First two bytes is the number of bools that are true. Then, it's at minimum that number of bools, set to true. The rest of the message are the bools that are false.
        SubmitBools = 2,
        SubmitFloat = 3,
        SubmitFloats = 4,
        SubmitHybrid = 5
    }
}
