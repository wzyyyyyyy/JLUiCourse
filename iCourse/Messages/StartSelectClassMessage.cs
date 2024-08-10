using iCourse.Models;

namespace iCourse.Messages
{
    internal class StartSelectClassMessage(BatchInfo batchInfo)
    {
        public BatchInfo BatchInfo { get; private set; } = batchInfo;
    }
}
