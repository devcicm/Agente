using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IPC.DB.Models
{
    public class DbCommandRequest
    {
        [Required]
        public string Sql { get; set; } = string.Empty;

        public Dictionary<string, object?>? Parameters { get; set; }
    }
}
