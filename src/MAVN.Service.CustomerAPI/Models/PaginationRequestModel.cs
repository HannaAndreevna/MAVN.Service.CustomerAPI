﻿using System.ComponentModel.DataAnnotations;

namespace MAVN.Service.CustomerAPI.Models
{
    /// <summary>
    /// Hold information about the Current page and the amount of items on each page
    /// </summary>
    public class PaginationRequestModel
    {
        /// <summary>The Current Page</summary>
        [Range(1, 10000)]
        [Required]
        public int CurrentPage { get; set; }

        /// <summary>The amount of items that the page holds</summary>
        [Range(1, 500)]
        [Required]
        public int PageSize { get; set; }
    }
}
