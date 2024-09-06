using Newtonsoft.Json.Linq;

namespace iCourse.Models
{
    public enum ClassSelectType
    {
        Elective, RestrictedElective
    }

    public class Course
    {
        public Course(JToken json)
        {
            Id = json["KXH"].Value<int>();
            Name = json["KCM"].ToString();
            TeacherName = json["SKJS"].ToString();
            Campus = json["XQ"].ToString();
            ClassLocation = json["YPSJDD"].ToString();
            SecretVal = json["secretVal"].ToString();
            SelectType = json["KCXZ"].ToString() switch
            {
                "选修" => ClassSelectType.Elective,
                "限选" => ClassSelectType.RestrictedElective,
                _ => SelectType
            };
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public string TeacherName { get; set; }
        public string Campus { get; set; }
        public ClassSelectType SelectType { get; set; }
        public string ClassLocation { get; set; }
        public string SecretVal { get; set; }
    }
}
