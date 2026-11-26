using System;
using System.Collections.Generic;
using System.Text;

namespace CardControl.Model.Downstream
{
    public sealed record DownstreamProblem(string? Code, string? Detail);
}
