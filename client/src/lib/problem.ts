import type { ProblemDetails } from '../types'

export class ApiError extends Error {
  readonly status: number
  readonly title: string

  constructor(status: number, title: string, detail: string) {
    super(detail || title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
  }
}

export async function readProblem(response: Response): Promise<ApiError> {
  let title = response.statusText || 'Request failed'
  let detail = title
  try {
    const body = (await response.json()) as ProblemDetails
    if (body.title) title = body.title
    if (body.detail) detail = body.detail
  } catch {
    detail = title
  }
  return new ApiError(response.status, title, detail)
}

export function messageFor(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) {
      return 'Too many login attempts. Wait a minute and try again.'
    }
    if (error.status === 409) {
      return error.message || 'This item is out of stock or was just purchased by someone else.'
    }
    if (error.status === 401) {
      return error.message || 'Sign in to continue.'
    }
    if (error.status === 403) {
      return error.message || 'You do not have permission to do that.'
    }
    return error.message
  }
  if (error instanceof Error && error.message) return error.message
  return 'Something went wrong. Try again.'
}
