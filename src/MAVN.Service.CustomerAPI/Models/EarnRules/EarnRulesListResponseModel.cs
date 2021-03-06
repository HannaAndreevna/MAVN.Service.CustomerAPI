﻿using System.Collections.Generic;
using JetBrains.Annotations;

namespace MAVN.Service.CustomerAPI.Models.EarnRules
{
    /// <summary>
    /// Represents Earn Rules
    /// </summary>
    [PublicAPI]
    public class EarnRulesListResponseModel
    {
        /// <summary>Earn Rules</summary>
        public IEnumerable<EarnRuleModel> EarnRules { get; set; }

        /// <summary>The current page number</summary>
        public int CurrentPage { get; set; }

        /// <summary>Size of a page</summary>
        public int PageSize { get; set; }

        /// <summary>Total count</summary>
        public int TotalCount { get; set; }
    }
}
