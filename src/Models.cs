using System.Collections.Generic;

namespace AutoElectiveOrb
{
    internal sealed class AppSettings
    {
        public string StudentId { get; set; }
        public bool DualDegree { get; set; }
        public string Identity { get; set; }
        public double RefreshInterval { get; set; }
        public bool ScheduledStart { get; set; }
        public string StartAt { get; set; }
        public int OrbX { get; set; }
        public int OrbY { get; set; }
        public List<CourseSetting> Courses { get; set; }

        public AppSettings()
        {
            StudentId = string.Empty;
            Identity = "bzx";
            RefreshInterval = 6;
            StartAt = "08:00:00";
            OrbX = -1;
            OrbY = -1;
            Courses = new List<CourseSetting>();
        }
    }

    internal sealed class CourseSetting
    {
        public string Name { get; set; }
        public int ClassNo { get; set; }
        public string School { get; set; }
        public int Threshold { get; set; }
        public int Priority { get; set; }
        public string SwapGroup { get; set; }
        public string DropName { get; set; }
        public int DropClassNo { get; set; }
        public string DropSchool { get; set; }

        public CourseSetting()
        {
            Name = string.Empty;
            ClassNo = 1;
            Priority = 100;
            School = string.Empty;
            SwapGroup = string.Empty;
            DropName = string.Empty;
            DropSchool = string.Empty;
        }

        public bool IsSwap { get { return !string.IsNullOrWhiteSpace(DropName); } }
    }

    internal sealed class CatalogCourse
    {
        public string Name { get; set; }
        public int ClassNo { get; set; }
        public string School { get; set; }
        public string Teacher { get; set; }
        public int MaxQuota { get; set; }
        public int UsedQuota { get; set; }
        public int RemainingQuota { get; set; }
        public bool QuotaKnown { get; set; }
        public string Outcome { get; set; }
        public bool? Selected { get; set; }

        public string Key { get { return (Name ?? string.Empty) + "\u001f" + ClassNo + "\u001f" + (School ?? string.Empty); } }
    }

    internal sealed class CatalogResult
    {
        public List<CatalogCourse> Elected { get; set; }
        public List<CatalogCourse> Plans { get; set; }
        public string Phase { get; set; }
        public bool CanExecuteSwap { get; set; }

        public CatalogResult()
        {
            Elected = new List<CatalogCourse>();
            Plans = new List<CatalogCourse>();
            Phase = string.Empty;
        }
    }

    internal sealed class LotteryResult
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public int TotalCount { get; set; }
        public int SelectedCount { get; set; }
        public int NotSelectedCount { get; set; }
        public int PendingCount { get; set; }
        public int UnknownCount { get; set; }
        public List<CatalogCourse> Results { get; set; }

        public LotteryResult()
        {
            Status = string.Empty;
            Message = string.Empty;
            Results = new List<CatalogCourse>();
        }
    }

    internal enum EngineState
    {
        Idle,
        Starting,
        Waiting,
        Running,
        Stopping,
        Failed
    }
}
