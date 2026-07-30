import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PmButton } from '../ui/button/button.directive';

@Component({
  selector: 'pm-button-gallery',
  imports: [PmButton, RouterLink],
  templateUrl: './button-gallery.html',
  styleUrl: './gallery-page.css',
})
export class ButtonGallery {}
