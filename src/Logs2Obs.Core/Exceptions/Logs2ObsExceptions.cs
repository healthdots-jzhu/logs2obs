namespace Logs2Obs.Core.Exceptions;

public abstract class Logs2ObsException(string message) : Exception(message);
public class ValidationException(string message)         : Logs2ObsException(message);
public class SqlSafetyException(string message)          : Logs2ObsException(message);
public class QueryGuardException(string message)         : Logs2ObsException(message);
public class TenantNotFoundException(string tenantId)    : Logs2ObsException($"Tenant '{tenantId}' not found.");
public class SchemaIncompatibleException(string message) : Logs2ObsException(message);
public class AiQueryException(string message)            : Logs2ObsException(message);
public class ReplayException(string message)             : Logs2ObsException(message);
public class IdempotencyException(string message)        : Logs2ObsException(message);
