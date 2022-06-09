using System;

namespace DMVService;

public class Ticket
{
    public string plate { get; set; }

    public string date { get; set; }

    public string violation { get; set; }

    public int amount { get; set; }

    public string location { get; set; }
}