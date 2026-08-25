namespace Siri.Modules.Commerce.Domain;

// Enum TYPE and MEMBERS stay normal PascalCase (deliberate exception — only the property HOLDING the
// enum, ORDER.STATUS, is uppercase; enum members flow into JSON via the global JsonStringEnumConverter
// registered in Siri.Api/Program.cs (see task P1-03's CLAUDE.md entry), uppercasing them would be an
// unrequested wire-contract change).
public enum OrderStatus { Pending, AwaitingPayment, Paid, Failed, Cancelled, Refunded }
