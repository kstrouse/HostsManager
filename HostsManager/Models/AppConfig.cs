using System.Collections.Generic;

namespace HostsManager.Models;

public class AppConfig
{
    public bool MinimizeToTrayOnClose { get; set; }

    public List<HostProfile> Profiles { get; set; } = [];
}
