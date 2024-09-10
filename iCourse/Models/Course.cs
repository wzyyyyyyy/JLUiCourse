using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace iCourse.Models
{
    public enum ClassSelectType
    {
        Elective, RestrictedElective
    }

    public class Course : ObservableObject
    {
        public Course()
        {
        }

        public Course(JToken json)
        {
            Name = $"{json["KCM"]} [{json["KXH"]}]";
            TeacherName = json["SKJS"].ToString();
            Campus = json["XQ"].ToString();
            ClassLocation = json["YPSJDD"].ToString();
            SecretVal = json["secretVal"].ToString();
            CourseId = json["JXBID"].ToString();

            SelectType = json["KCXZ"].ToString() switch
            {
                "选修" => ClassSelectType.Elective,
                "限选" => ClassSelectType.RestrictedElective,
                _ => SelectType
            };
        }

        public string Name { get; set; }
        public string CourseId { get; set; }
        public string TeacherName { get; set; }
        public string Campus { get; set; }
        public ClassSelectType SelectType { get; set; }
        public string ClassLocation { get; set; }
        public string SecretVal { get; set; }
    }
}
