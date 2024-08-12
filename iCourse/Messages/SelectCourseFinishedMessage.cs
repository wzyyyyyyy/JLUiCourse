using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace iCourse.Messages
{
    record SelectCourseFinishedMessage(int FinishedNum, int Total);
}
