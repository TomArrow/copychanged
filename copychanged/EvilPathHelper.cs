using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace copychanged
{
    // replaces each segment of the path that is evil with a placeholder, and then reverses this again
    // that way we can use windows functions like getfullpath without it silently changing the path we input (by removing trailing slashes or dots)
    class EvilPathHelper
    {

    }
}
