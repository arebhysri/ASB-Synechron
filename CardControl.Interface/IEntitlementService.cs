using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Interface
{
    public interface IEntitlementService
    {
        bool IsCorporateEnabled(string corporateId);
    }
}
