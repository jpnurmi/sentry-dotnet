#include "sentry_dotnet_logger.h"

#include <stdio.h>
#include <string.h>

static
const char *
describe_level(sentry_level_t level)
{
    switch (level) {
    case SENTRY_LEVEL_DEBUG:
        return "Debug: ";
    case SENTRY_LEVEL_INFO:
        return "Info: ";
    case SENTRY_LEVEL_WARNING:
        return "Warning: ";
    case SENTRY_LEVEL_ERROR:
        return "Error: ";
    case SENTRY_LEVEL_FATAL:
        return "Fatal: ";
    default:
        return "Unknown: ";
    }
}

static void
sentry_dotnet_logger(
    sentry_level_t level, const char *message, va_list args, void *data)
{
    fprintf(stderr, "### sentry_dotnet_logger level=%d, message=%s\n", level, message);

    (void)data;
    const char *prefix = describe_level(level);
    const char *tag = "[native] ";

    size_t len = strlen(prefix) + strlen(tag)
        + (message ? strlen(message) : 0) + 2;
    char *format = sentry_malloc(len);
    if (!format) {
        fprintf(stderr, "### format null\n");
        return;
    }
    snprintf(format, len, "%s%s%s\n", prefix, tag, message);

    fprintf(stderr, "### format %s\n", format);

    vfprintf(stderr, format, args);

    sentry_free(format);
}

void
sentry_options_set_dotnet_logger(sentry_options_t *options)
{
    fprintf(stderr, "### sentry_options_set_dotnet_logger\n");

    sentry_options_set_logger(options, sentry_dotnet_logger, NULL);
}
