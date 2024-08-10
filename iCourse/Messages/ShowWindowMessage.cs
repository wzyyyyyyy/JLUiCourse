using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace iCourse.Messages
{
    class ShowWindowMessage(Type viewModelType, params Object[] args)
    {
        public Type ViewModelType { get; } = viewModelType;
        public Object[] Args { get; } = args;
    }
}
