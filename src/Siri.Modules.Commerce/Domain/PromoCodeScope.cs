namespace Siri.Modules.Commerce.Domain;

// Enum TYPE and MEMBERS stay normal PascalCase — same reasoning as OrderStatus. Members are an
// inferred-but-reasonable scaffold decision: docs/DATABASE.md's sketch lists a "Scope, ScopeRefId"
// column pair without enumerating Scope's values. AllCourses/Category/Course/Bundle covers every
// purchasable/browsable grouping that exists in this codebase at scaffolding time.
public enum PromoCodeScope { AllCourses, Category, Course, Bundle }
