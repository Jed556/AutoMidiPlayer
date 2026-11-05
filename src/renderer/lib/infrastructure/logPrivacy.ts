/** Diagnostic output only: never use the result to open or persist a file. */
export function redactLogPaths(message: string): string {
  return message
    .replace(/(?:\\{2,4}\?\\+)?[a-z]:[\\/]+Users[\\/]+[^\\/\r\n"<>|?#]+/gi, '%USERPROFILE%')
    .replace(/\/(?:home|Users)\/[^\\/\r\n"<>|?#]+/g, () => '$HOME');
}
