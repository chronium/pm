import { Component } from '@angular/core';

import { PmFormField } from '../ui/form-field/form-field';

@Component({
  selector: 'pm-form-gallery',
  imports: [PmFormField],
  template: `
    <section class="component-page pm-frosted-surface pm-scroll-surface pm-component-surface">
      <header class="component-header">
        <p>Foundation</p>
        <h1>Form fields</h1>
      </header>

      <section class="specimen" aria-labelledby="form-inputs">
        <h2 id="form-inputs">Inputs</h2>
        <div class="specimen-content specimen-content--column specimen-content--constrained">
          <pm-form-field>
            <label for="gallery-task-title">Task title</label>
            <input pmControl id="gallery-task-title" value="Refine the component gallery" />
          </pm-form-field>
          <pm-form-field>
            <label for="gallery-task-id">Task ID</label>
            <input
              pmControl
              id="gallery-task-id"
              aria-describedby="gallery-task-id-hint"
              value="PM-0073"
            />
            <p pmFieldMessage id="gallery-task-id-hint">IDs are assigned when tasks are created.</p>
          </pm-form-field>
        </div>
      </section>

      <section class="specimen" aria-labelledby="form-selection">
        <h2 id="form-selection">Selection</h2>
        <div class="specimen-content specimen-content--column specimen-content--constrained">
          <pm-form-field>
            <label for="gallery-status">Status</label>
            <select pmControl id="gallery-status">
              <option>To Do</option>
              <option>In Progress</option>
              <option>Done</option>
            </select>
          </pm-form-field>
          <pm-form-field>
            <label for="gallery-description">Description</label>
            <textarea pmControl id="gallery-description" rows="4">
A compact multiline field.</textarea>
          </pm-form-field>
        </div>
      </section>

      <section class="specimen" aria-labelledby="form-states">
        <h2 id="form-states">States</h2>
        <div class="specimen-content specimen-content--column specimen-content--constrained">
          <pm-form-field>
            <label for="gallery-invalid">Task title</label>
            <input
              pmControl
              id="gallery-invalid"
              aria-invalid="true"
              aria-describedby="gallery-invalid-message"
            />
            <p pmFieldMessage id="gallery-invalid-message" role="alert">
              Enter a title before saving the task.
            </p>
          </pm-form-field>
          <pm-form-field>
            <label for="gallery-disabled">Track</label>
            <input pmControl id="gallery-disabled" value="PM" disabled />
          </pm-form-field>
        </div>
      </section>
    </section>
  `,
  styleUrl: './gallery-page.css',
})
export class FormGallery {}
