import { describe, expect, it } from 'vitest'
import { problemMessage } from './locations'

const json = (body: unknown, status = 409) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } })

describe('problemMessage', () => {
  it('prefers the first validation error', () => {
    const response = json({ title: 'One or more validation errors occurred.', errors: { name: ['name is required.'] } }, 400)

    return expect(problemMessage(response)).resolves.toBe('name is required.')
  })

  it('then the detail', () => {
    const response = json({ title: 'Can’t save that location', detail: 'You already have a location called "Home".' })

    return expect(problemMessage(response)).resolves.toBe('You already have a location called "Home".')
  })

  it('then the title', () => {
    return expect(problemMessage(json({ title: 'Unauthorized' }, 401))).resolves.toBe('Unauthorized')
  })

  it('falls back to the status when the body is not JSON', () => {
    const response = new Response('<html>Bad gateway</html>', { status: 502, statusText: 'Bad Gateway' })

    return expect(problemMessage(response)).resolves.toBe('Bad Gateway')
  })
})
