﻿namespace iCourse.Models
{
    public class BatchInfo
    {
        public string batchId { get; set; }
        public string batchName { get; set; }
        public string beginTime { get; set; }
        public string endTime { get; set; }
        public string tacticName { get; set; }
        public string noSelectReason { get; set; }
        public string typeName { get; set; }
        public bool canSelect { get; set; }

    }
}